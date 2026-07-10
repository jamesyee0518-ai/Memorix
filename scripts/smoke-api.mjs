#!/usr/bin/env node

const baseUrl = (process.env.API_BASE_URL || "http://localhost:9101").replace(/\/$/, "");
const runProcessing = process.env.SMOKE_RUN_PROCESSING === "1";
const timeoutMs = Number(process.env.SMOKE_TIMEOUT_MS || 10000);

const state = {
  token: "",
  topicId: "",
  sourceId: "",
  documentId: "",
};

function value(obj, key) {
  if (!obj || typeof obj !== "object") return undefined;
  return obj[key] ?? obj[key.charAt(0).toUpperCase() + key.slice(1)];
}

function unwrap(response) {
  const success = value(response, "success");
  if (success === false) {
    const error = value(response, "error");
    throw new Error(`${value(error, "code") || "API_ERROR"}: ${value(error, "message") || "request failed"}`);
  }
  return value(response, "data") ?? response;
}

async function request(method, path, body, { auth = true, expectJson = true } = {}) {
  const headers = {};
  if (body !== undefined) headers["content-type"] = "application/json";
  if (auth && state.token) headers.authorization = `Bearer ${state.token}`;

  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(timeoutMs),
  });

  const text = await response.text();
  let payload = null;
  if (text && expectJson) {
    try {
      payload = JSON.parse(text);
    } catch {
      throw new Error(`${method} ${path} returned non-JSON response: ${text.slice(0, 200)}`);
    }
  }

  if (!response.ok) {
    const message = payload ? JSON.stringify(payload) : text;
    throw new Error(`${method} ${path} failed with ${response.status}: ${message}`);
  }

  return payload;
}

async function step(name, fn) {
  process.stdout.write(`- ${name} ... `);
  await fn();
  process.stdout.write("ok\n");
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function poll(name, fn, { attempts = 20, delayMs = 1500 } = {}) {
  for (let i = 0; i < attempts; i += 1) {
    const result = await fn();
    if (result) return result;
    await new Promise((resolve) => setTimeout(resolve, delayMs));
  }
  throw new Error(`${name} did not complete after ${attempts} attempts`);
}

async function getDiagnostics() {
  const lines = [];
  if (state.sourceId) {
    try {
      const source = unwrap(await request("GET", `/api/Sources/${state.sourceId}`));
      lines.push(`source.status=${value(source, "status") || "unknown"}`);
      const errorMessage = value(source, "errorMessage");
      if (errorMessage) lines.push(`source.error=${errorMessage}`);
    } catch (error) {
      lines.push(`source.diagnostic_failed=${error?.message || error}`);
    }
  }
  if (state.topicId) {
    try {
      const documents = unwrap(await request("GET", `/api/Documents?topicId=${state.topicId}&page=1&pageSize=5`));
      const items = value(documents, "items") || [];
      lines.push(`documents.count=${items.length}`);
      if (items[0]) {
        lines.push(`latest_document.aiStatus=${value(items[0], "aiStatus") || "unknown"}`);
        lines.push(`latest_document.parseStatus=${value(items[0], "parseStatus") || "unknown"}`);
        lines.push(`latest_document.indexStatus=${value(items[0], "indexStatus") || "unknown"}`);
      }
    } catch (error) {
      lines.push(`documents.diagnostic_failed=${error?.message || error}`);
    }
  }
  return lines.length ? `\nDiagnostics:\n  ${lines.join("\n  ")}` : "";
}

async function main() {
  const unique = Date.now();
  const email = `smoke-${unique}@memorix.local`;
  const password = "SmokeTest123!";
  const title = `Smoke Source ${unique}`;
  const keyword = `memorix-smoke-${unique}`;

  console.log(`Memorix API smoke test`);
  console.log(`Base URL: ${baseUrl}`);
  console.log(`AI processing steps: ${runProcessing ? "enabled" : "skipped"}`);

  await step("health endpoint", async () => {
    const health = await request("GET", "/health", undefined, { auth: false });
    assert(value(health, "status") === "healthy", "health endpoint did not return healthy");
  });

  await step("register user", async () => {
    const payload = unwrap(await request("POST", "/api/Auth/register", {
      email,
      password,
      nickname: "Smoke Tester",
    }, { auth: false }));
    state.token = value(payload, "token");
    assert(state.token, "register response did not include token");
  });

  await step("current user", async () => {
    const payload = unwrap(await request("GET", "/api/Auth/me"));
    assert(value(payload, "email") === email, "current user email mismatch");
  });

  await step("create topic", async () => {
    const payload = unwrap(await request("POST", "/api/Topics", {
      name: `Smoke Topic ${unique}`,
      description: "Automated smoke-test topic",
      domain: "smoke",
      visibility: "private",
    }));
    state.topicId = value(payload, "id");
    assert(state.topicId, "create topic response did not include id");
  });

  await step("list topics", async () => {
    const payload = unwrap(await request("GET", "/api/Topics?page=1&pageSize=50"));
    const items = value(payload, "items") || [];
    assert(items.some((item) => value(item, "id") === state.topicId), "created topic not found in topic list");
  });

  await step("import text source", async () => {
    const payload = unwrap(await request("POST", "/api/Sources/text", {
      topicId: state.topicId,
      title,
      content: [
        "This is an automated Memorix smoke-test source.",
        `Unique keyword: ${keyword}.`,
        "It verifies auth, topic creation, source import, and source listing without requiring an LLM.",
      ].join("\n"),
    }));
    state.sourceId = value(payload, "id");
    assert(state.sourceId, "import text response did not include id");
  });

  await step("read imported source", async () => {
    const payload = unwrap(await request("GET", `/api/Sources/${state.sourceId}`));
    assert(value(payload, "title") === title, "source title mismatch");
    assert(value(payload, "sourceType") === "text", "source type mismatch");
  });

  await step("list imported source", async () => {
    const payload = unwrap(await request("GET", `/api/Sources?topicId=${state.topicId}&page=1&pageSize=50`));
    const items = value(payload, "items") || [];
    assert(items.some((item) => value(item, "id") === state.sourceId), "imported source not found in source list");
  });

  if (runProcessing) {
    await step("trigger source processing", async () => {
      unwrap(await request("POST", `/api/Sources/${state.sourceId}/process`, {}));
    });

    await step("wait for document", async () => {
      let document;
      try {
        document = await poll("document creation", async () => {
          const payload = unwrap(await request("GET", `/api/Documents?topicId=${state.topicId}&page=1&pageSize=20`));
          const items = value(payload, "items") || [];
          return items.find((item) => value(item, "sourceId") === state.sourceId);
        });
      } catch (error) {
        throw new Error(`${error.message}${await getDiagnostics()}`);
      }
      state.documentId = value(document, "id");
      assert(state.documentId, "document list item did not include id");
    });

    await step("keyword search", async () => {
      const payload = unwrap(await request("POST", "/api/Search", {
        topicId: state.topicId,
        query: keyword,
        searchType: "keyword",
        limit: 10,
      }));
      const items = value(payload, "items") || [];
      assert(items.length > 0, "keyword search returned no results");
    });
  }

  console.log("\nSmoke test passed.");
}

main().catch((error) => {
  console.error("\nSmoke test failed.");
  console.error(error?.message || error);
  console.error("\nBefore running this script, start dependencies and the API:");
  console.error("  docker compose up -d postgres minio");
  console.error("  dotnet run --project src/KnowledgeEngine.Api/KnowledgeEngine.Api.csproj --launch-profile http");
  console.error("\nIf /health is healthy but register/import still fails, stop any old API process and start it again.");
  process.exit(1);
});
