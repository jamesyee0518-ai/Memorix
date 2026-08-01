/**
 * WebSocket client for the Memorix TranscriptionHub.
 *
 * Connects to the SignalR hub at /hubs/transcription (mapped in Program.cs)
 * to enable real-time streaming transcription. Clients send audio chunks
 * and receive partial/final transcription results.
 *
 * Uses the @microsoft/signalr package when available. Falls back to a raw
 * WebSocket implementation that speaks the SignalR JSON hub protocol for
 * environments where the signalr package is not installed.
 *
 * Hub methods (server -> client):
 *   - "SessionStarted"     -> onSessionStarted
 *   - "PartialResult"      -> onPartialResult
 *   - "TranscriptionComplete" -> onTranscriptionComplete
 *   - "ChunkReceived"      -> onChunkReceived
 *   - "Error"              -> onError
 *   - "SessionEnded"       -> (internal)
 *
 * Hub methods (client -> server):
 *   - StartSession(request)
 *   - SendAudioChunk(chunk)
 *   - EndSession()
 */

import { TRANSCRIPTION_HUB_URL, CLIENT_VERSION } from "../config";
import { getAccessToken } from "../storage/auth";
import type {
  AudioChunkMessage,
  ChunkReceivedEvent,
  HubErrorEvent,
  PartialResultEvent,
  SessionEndedEvent,
  SessionStartedEvent,
  StartSessionRequest,
  TranscriptionCompleteEvent,
} from "../types/audio";

// ═══════════════════════════════════════════════════════════════════════════
// TYPE DEFINITIONS FOR @microsoft/signalr INTEROP
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Minimal type interface for the @microsoft/signalr HubConnection.
 * This allows the module to compile without installing the package,
 * while using it at runtime when available.
 */
interface SignalRHubConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, handler: (...args: unknown[]) => void): void;
  invoke<T = unknown>(methodName: string, ...args: unknown[]): Promise<T>;
  onclose(callback: (error?: Error) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
}

interface SignalRHubConnectionBuilder {
  withUrl(url: string, options?: {
    accessTokenFactory?: () => string | Promise<string>;
    headers?: Record<string, string>;
  }): SignalRHubConnectionBuilder;
  withAutomaticReconnect(): SignalRHubConnectionBuilder;
  configureLogging(level: unknown): SignalRHubConnectionBuilder;
  build(): SignalRHubConnection;
}

type SignalRModule = {
  HubConnectionBuilder: new () => SignalRHubConnectionBuilder;
  LogLevel: {
    Trace: 0;
    Debug: 1;
    Information: 2;
    Warning: 3;
    Error: 4;
    Critical: 5;
    None: 6;
  };
  HttpTransportType: {
    WebSockets: 1;
    ServerSentEvents: 2;
    LongPolling: 4;
  };
};

// ═══════════════════════════════════════════════════════════════════════════
// EVENT HANDLER TYPE ALIASES
// ═══════════════════════════════════════════════════════════════════════════

export type SessionStartedHandler = (event: SessionStartedEvent) => void;
export type PartialResultHandler = (event: PartialResultEvent) => void;
export type TranscriptionCompleteHandler = (event: TranscriptionCompleteEvent) => void;
export type ChunkReceivedHandler = (event: ChunkReceivedEvent) => void;
export type ErrorHandler = (event: HubErrorEvent) => void;

// ═══════════════════════════════════════════════════════════════════════════
// TRANSCRIPTION HUB CLIENT
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Client for the TranscriptionHub SignalR endpoint.
 *
 * Primary transport: @microsoft/signalr HubConnection (when the package
 * is installed via `npm install @microsoft/signalr`).
 * Fallback transport: raw WebSocket implementing the SignalR JSON hub protocol.
 *
 * Usage:
 *   const client = new TranscriptionHubClient();
 *   client.onPartialResult = (e) => console.log(e.finalText);
 *   await client.connect();
 *   await client.startSession({ language: "zh", enablePunctuation: true });
 *   client.sendAudioChunk({ chunkIndex: 0, data: uint8Array, isFinal: false, ... });
 *   await client.endSession();
 *   await client.disconnect();
 */
export class TranscriptionHubClient {
  /** Event: session started, provider resolved. */
  onSessionStarted: SessionStartedHandler | null = null;

  /** Event: partial or final transcription result received. */
  onPartialResult: PartialResultHandler | null = null;

  /** Event: transcription fully completed. */
  onTranscriptionComplete: TranscriptionCompleteHandler | null = null;

  /** Event: server acknowledged receipt of an audio chunk. */
  onChunkReceived: ChunkReceivedHandler | null = null;

  /** Event: error from the hub. */
  onError: ErrorHandler | null = null;

  private hubConnection: SignalRHubConnection | null = null;
  private ws: WebSocket | null = null;
  private useSignalR: boolean = false;
  private connectionId: string | null = null;
  private nextInvocationId: number = 1;
  private pendingInvocations: Map<
    string,
    { resolve: (value: unknown) => void; reject: (error: Error) => void }
  > = new Map();

  /**
   * Attempts to dynamically import @microsoft/signalr. Returns the module
   * if available, or null if the package is not installed.
   */
  private static async tryLoadSignalR(): Promise<SignalRModule | null> {
    try {
      // Use dynamic import so the package is only required when actually used.
      // The @ts-expect-error suppresses the "cannot find module" error at
      // compile time; at runtime the import either resolves (package installed)
      // or throws (caught below), triggering the raw WebSocket fallback.
      // @ts-expect-error - @microsoft/signalr is an optional peer dependency
      const mod = await import("@microsoft/signalr");
      return mod as unknown as SignalRModule;
    } catch {
      return null;
    }
  }

  /**
   * Connects to the TranscriptionHub. Uses @microsoft/signalr if available;
   * otherwise falls back to a raw WebSocket.
   */
  async connect(): Promise<void> {
    const signalR = await TranscriptionHubClient.tryLoadSignalR();

    if (signalR) {
      await this.connectWithSignalR(signalR);
    } else {
      await this.connectWithRawWebSocket();
    }
  }

  /**
   * Disconnects from the hub.
   */
  async disconnect(): Promise<void> {
    if (this.hubConnection) {
      await this.hubConnection.stop().catch(() => {});
      this.hubConnection = null;
    }
    if (this.ws) {
      this.ws.close(1000, "Client disconnecting");
      this.ws = null;
    }
    this.useSignalR = false;
    this.connectionId = null;
  }

  /**
   * Starts a streaming transcription session.
   * The hub resolves the best ASR provider and returns a SessionStarted event.
   */
  async startSession(request: StartSessionRequest): Promise<void> {
    if (this.useSignalR && this.hubConnection) {
      await this.hubConnection.invoke("StartSession", request);
    } else if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      await this.invokeRaw("StartSession", [request]);
    } else {
      throw new Error("TranscriptionHub client is not connected. Call connect() first.");
    }
  }

  /**
   * Sends an audio chunk to the hub for streaming transcription.
   * When isFinal is true, the hub triggers transcription of buffered audio.
   */
  async sendAudioChunk(chunk: AudioChunkMessage): Promise<void> {
    if (this.useSignalR && this.hubConnection) {
      await this.hubConnection.invoke("SendAudioChunk", chunk);
    } else if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      await this.invokeRaw("SendAudioChunk", [chunk]);
    } else {
      throw new Error("TranscriptionHub client is not connected. Call connect() first.");
    }
  }

  /**
   * Ends the streaming transcription session. If there is buffered audio
   * that hasn't been transcribed, the hub processes it before ending.
   */
  async endSession(): Promise<void> {
    if (this.useSignalR && this.hubConnection) {
      await this.hubConnection.invoke("EndSession");
    } else if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      await this.invokeRaw("EndSession", []);
    }
  }

  // ═════════════════════════════════════════════════════════════════════════
  // SIGNALR TRANSPORT
  // ═════════════════════════════════════════════════════════════════════════

  /**
   * Connects using the @microsoft/signalr HubConnectionBuilder.
   */
  private async connectWithSignalR(signalR: SignalRModule): Promise<void> {
    const token = getAccessToken();

    const builder = new signalR.HubConnectionBuilder();
    const connection = builder
      .withUrl(TRANSCRIPTION_HUB_URL, {
        accessTokenFactory: () => token ?? "",
        headers: {
          "X-Memorix-Client-Version": CLIENT_VERSION,
        },
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Register event handlers
    connection.on("SessionStarted", (...args: unknown[]) => {
      const payload = args[0] as SessionStartedEvent;
      this.onSessionStarted?.(payload);
    });

    connection.on("PartialResult", (...args: unknown[]) => {
      const payload = args[0] as PartialResultEvent;
      this.onPartialResult?.(payload);
    });

    connection.on("TranscriptionComplete", (...args: unknown[]) => {
      const payload = args[0] as TranscriptionCompleteEvent;
      this.onTranscriptionComplete?.(payload);
    });

    connection.on("ChunkReceived", (...args: unknown[]) => {
      const payload = args[0] as ChunkReceivedEvent;
      this.onChunkReceived?.(payload);
    });

    connection.on("Error", (...args: unknown[]) => {
      const payload = args[0] as HubErrorEvent;
      this.onError?.(payload);
    });

    connection.on("SessionEnded", (...args: unknown[]) => {
      const payload = args[0] as SessionEndedEvent;
      // SessionEnded is informational; no external handler required.
      void payload;
    });

    connection.onclose((error?: Error) => {
      if (error) {
        this.onError?.({ message: error.message });
      }
    });

    await connection.start();

    this.hubConnection = connection;
    this.useSignalR = true;
  }

  // ═════════════════════════════════════════════════════════════════════════
  // RAW WEBSOCKET FALLBACK (implements SignalR JSON hub protocol)
  // ═════════════════════════════════════════════════════════════════════════

  /**
   * Connects using a raw WebSocket, speaking the SignalR JSON hub protocol.
   * This is a minimal implementation that supports:
   *   - Handshake (protocol negotiation)
   *   - Invocation (client -> server method calls)
   *   - StreamItem / Completion (server -> client responses)
   *   - Server-sent invocations (events like "PartialResult")
   */
  private async connectWithRawWebSocket(): Promise<void> {
    const token = getAccessToken();

    // Build the WebSocket URL with SignalR query parameters
    const wsUrl = new URL(TRANSCRIPTION_HUB_URL);
    wsUrl.protocol = wsUrl.protocol === "https:" ? "wss:" : "ws:";
    wsUrl.searchParams.set("access_token", token ?? "");

    return new Promise<void>((resolve, reject) => {
      this.ws = new WebSocket(wsUrl.toString());

      this.ws.binaryType = "arraybuffer";

      this.ws.onopen = () => {
        // Send SignalR handshake: {"protocol":"json","version":1}\x1e
        const handshake = JSON.stringify({ protocol: "json", version: 1 }) + "\x1e";
        this.ws?.send(handshake);
      };

      this.ws.onmessage = (event: MessageEvent) => {
        this.handleRawMessage(event.data);
      };

      this.ws.onerror = () => {
        this.onError?.({ message: "WebSocket connection error" });
      };

      this.ws.onclose = (event: CloseEvent) => {
        this.useSignalR = false;
        this.ws = null;
        if (!event.wasClean) {
          this.onError?.({
            message: `WebSocket closed unexpectedly: ${event.code} ${event.reason}`,
          });
        }
      };

      // Wait for the handshake response before resolving.
      // The handshake response is "{}\x1e" (empty object) on success.
      const handshakeCheck = (event: MessageEvent) => {
        const data = event.data;
        if (typeof data === "string" && data.includes("{}\x1e")) {
          this.ws?.removeEventListener("message", handshakeCheck);
          this.useSignalR = false;
          resolve();
        } else if (typeof data === "string" && data.includes("error")) {
          this.ws?.removeEventListener("message", handshakeCheck);
          reject(new Error("SignalR handshake failed"));
        }
      };

      this.ws.addEventListener("message", handshakeCheck);

      // Timeout if handshake doesn't complete
      setTimeout(() => {
        if (this.ws && this.ws.readyState === WebSocket.CONNECTING) {
          reject(new Error("WebSocket connection timed out"));
        }
      }, 15000);
    });
  }

  /**
   * Handles incoming raw WebSocket messages, parsing the SignalR JSON hub protocol.
   * Messages are delimited by \x1e (Record Separator).
   */
  private handleRawMessage(data: string | ArrayBuffer): void {
    if (typeof data !== "string") {
      // Binary messages are not expected in the JSON protocol
      return;
    }

    // Split on the record separator; each segment is a JSON message
    const messages = data.split("\x1e");
    for (const msg of messages) {
      if (!msg.trim()) continue;

      let parsed: unknown;
      try {
        parsed = JSON.parse(msg);
      } catch {
        continue; // Skip unparseable messages
      }

      const record = parsed as Record<string, unknown>;
      if (!record || typeof record !== "object") continue;

      const type = record["type"] as number | undefined;

      switch (type) {
        // Type 1: Invocation (server -> client event)
        case 1: {
          const target = record["target"] as string;
          const args = (record["arguments"] as unknown[]) ?? [];
          this.handleServerInvocation(target, args);
          break;
        }
        // Type 2: StreamItem
        case 2: {
          // Not used by TranscriptionHub, but handle for completeness
          break;
        }
        // Type 3: Completion (response to a client invocation)
        case 3: {
          const invocationId = record["invocationId"] as string;
          const error = record["error"] as string | undefined;
          const result = record["result"];
          const pending = this.pendingInvocations.get(invocationId);
          if (pending) {
            this.pendingInvocations.delete(invocationId);
            if (error) {
              pending.reject(new Error(error));
            } else {
              pending.resolve(result);
            }
          }
          break;
        }
        // Type 6: Ping (keep-alive)
        case 6: {
          // Respond with a ping to keep the connection alive
          const ping = JSON.stringify({ type: 6 }) + "\x1e";
          this.ws?.send(ping);
          break;
        }
        // Type 7: Close
        case 7: {
          const error = record["error"] as string | undefined;
          if (error) {
            this.onError?.({ message: error });
          }
          this.ws?.close();
          break;
        }
        default:
          // Unknown message type; ignore
          break;
      }
    }
  }

  /**
   * Dispatches a server-sent invocation (event) to the appropriate handler.
   */
  private handleServerInvocation(target: string, args: unknown[]): void {
    switch (target) {
      case "SessionStarted":
        this.onSessionStarted?.(args[0] as SessionStartedEvent);
        break;
      case "PartialResult":
        this.onPartialResult?.(args[0] as PartialResultEvent);
        break;
      case "TranscriptionComplete":
        this.onTranscriptionComplete?.(args[0] as TranscriptionCompleteEvent);
        break;
      case "ChunkReceived":
        this.onChunkReceived?.(args[0] as ChunkReceivedEvent);
        break;
      case "Error":
        this.onError?.(args[0] as HubErrorEvent);
        break;
      case "SessionEnded":
        // Informational; no external handler
        break;
      default:
        // Unknown event; ignore
        break;
    }
  }

  /**
   * Sends a hub invocation via the raw WebSocket and returns a promise that
   * resolves when the server sends a Completion message.
   */
  private invokeRaw(methodName: string, args: unknown[]): Promise<unknown> {
    const invocationId = String(this.nextInvocationId++);

    const message = JSON.stringify({
      type: 1, // Invocation
      invocationId,
      target: methodName,
      arguments: args,
    }) + "\x1e";

    return new Promise<unknown>((resolve, reject) => {
      if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
        reject(new Error("WebSocket is not connected"));
        return;
      }

      this.pendingInvocations.set(invocationId, { resolve, reject });
      this.ws.send(message);

      // Timeout: if no completion within 30 seconds, reject
      setTimeout(() => {
        if (this.pendingInvocations.has(invocationId)) {
          this.pendingInvocations.delete(invocationId);
          reject(new Error(`Invocation '${methodName}' timed out`));
        }
      }, 30000);
    });
  }

  // ═════════════════════════════════════════════════════════════════════════
  // CONNECTION STATE
  // ═════════════════════════════════════════════════════════════════════════

  /**
   * Returns true if the client is currently connected to the hub.
   */
  get isConnected(): boolean {
    if (this.useSignalR && this.hubConnection) {
      return true;
    }
    if (this.ws) {
      return this.ws.readyState === WebSocket.OPEN;
    }
    return false;
  }

  /**
   * Returns the current connection ID, or null if not connected.
   */
  get connectionIdValue(): string | null {
    return this.connectionId;
  }
}

// ═══════════════════════════════════════════════════════════════════════════
// CONVENIENCE FACTORY FUNCTION
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Creates and connects a new TranscriptionHubClient.
 * Returns the connected client ready for startSession/sendAudioChunk/endSession.
 *
 * @example
 *   const client = await createTranscriptionHubClient({
 *     onPartialResult: (e) => console.log(e.finalText),
 *     onError: (e) => console.error(e.message),
 *   });
 *   await client.startSession({ language: "zh", enablePunctuation: true, sampleRate: 16000 });
 */
export async function createTranscriptionHubClient(handlers?: {
  onSessionStarted?: SessionStartedHandler;
  onPartialResult?: PartialResultHandler;
  onTranscriptionComplete?: TranscriptionCompleteHandler;
  onChunkReceived?: ChunkReceivedHandler;
  onError?: ErrorHandler;
}): Promise<TranscriptionHubClient> {
  const client = new TranscriptionHubClient();

  if (handlers?.onSessionStarted) client.onSessionStarted = handlers.onSessionStarted;
  if (handlers?.onPartialResult) client.onPartialResult = handlers.onPartialResult;
  if (handlers?.onTranscriptionComplete) client.onTranscriptionComplete = handlers.onTranscriptionComplete;
  if (handlers?.onChunkReceived) client.onChunkReceived = handlers.onChunkReceived;
  if (handlers?.onError) client.onError = handlers.onError;

  await client.connect();
  return client;
}
