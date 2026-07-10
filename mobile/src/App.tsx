import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Platform,
  Pressable,
  SafeAreaView,
  StyleSheet,
  Text,
  TextInput,
  View,
} from "react-native";
import { Audio } from "expo-av";
import * as DocumentPicker from "expo-document-picker";
import { StatusBar } from "expo-status-bar";
import NetInfo from "@react-native-community/netinfo";
import { bindDevice, captureText, captureUpload, captureUrl, deactivateDevice, pairDevice } from "./api/client";
import { registerPushToken } from "./notifications/registerPushToken";
import { clearDeviceTokens, setDeviceAccessToken, setDeviceRefreshToken } from "./storage/auth";
import { getClientId } from "./storage/device";
import { enqueueCapture, flushQueue, readQueue } from "./storage/offlineQueue";

type Mode = "text" | "url" | "file" | "audio";

export default function App() {
  const [mode, setMode] = useState<Mode>("text");
  const [clientId, setClientId] = useState("");
  const [text, setText] = useState("");
  const [url, setUrl] = useState("");
  const [title, setTitle] = useState("");
  const [pairingCode, setPairingCode] = useState("");
  const [pendingCount, setPendingCount] = useState(0);
  const [failedCount, setFailedCount] = useState(0);
  const [recording, setRecording] = useState<Audio.Recording | null>(null);
  const [busy, setBusy] = useState(false);

  const modeLabel = useMemo(() => {
    if (mode === "text") return "文字";
    if (mode === "url") return "链接";
    if (mode === "audio") return "录音";
    return "文件";
  }, [mode]);

  useEffect(() => {
    async function boot() {
      const id = await getClientId();
      const pushToken = await registerPushToken().catch(() => undefined);
      setClientId(id);
      const binding = await bindDevice({
        clientId: id,
        deviceName: Platform.OS === "ios" ? "iPhone" : "Android",
        platform: Platform.OS,
        pushToken,
      }).catch(() => undefined);
      if (binding?.deviceAccessToken) {
        await setDeviceAccessToken(binding.deviceAccessToken);
        await setDeviceRefreshToken(binding.refreshToken);
      }
      await refreshQueue();
      await flushQueue().then(refreshQueue).catch(() => undefined);
    }
    boot();
    const unsubscribe = NetInfo.addEventListener((state) => {
      if (state.isConnected) {
        flushQueue().then(refreshQueue).catch(() => undefined);
      }
    });
    return unsubscribe;
  }, []);

  async function refreshQueue() {
    const queue = await readQueue();
    setPendingCount(queue.length);
    setFailedCount(queue.filter((item) => item.attempts > 0).length);
  }

  async function submitText() {
    const value = text.trim();
    if (!value) return;
    await submitOrQueue(() => captureText({ clientId, contentText: value }), {
      kind: "text",
      clientId,
      contentText: value,
    });
    setText("");
  }

  async function submitUrl() {
    const value = url.trim();
    if (!value) return;
    await submitOrQueue(() => captureUrl({ clientId, sourceUrl: value, title }), {
      kind: "url",
      clientId,
      sourceUrl: value,
      title: title.trim() || undefined,
    });
    setUrl("");
    setTitle("");
  }

  async function pickFile() {
    const result = await DocumentPicker.getDocumentAsync({ copyToCacheDirectory: true });
    if (result.canceled) return;
    const file = result.assets[0];
    await submitOrQueue(
      () =>
        captureUpload({
          clientId,
          uri: file.uri,
          name: file.name,
          mimeType: file.mimeType ?? "application/octet-stream",
        }),
      {
        kind: "upload",
        clientId,
        uri: file.uri,
        name: file.name,
        mimeType: file.mimeType ?? "application/octet-stream",
      }
    );
  }

  async function toggleRecording() {
    if (recording) {
      await recording.stopAndUnloadAsync();
      const uri = recording.getURI();
      setRecording(null);
      if (!uri) return;
      await submitOrQueue(
        () =>
          captureUpload({
            clientId,
            uri,
            name: `recording-${Date.now()}.m4a`,
            mimeType: "audio/mp4",
          }),
        {
          kind: "upload",
          clientId,
          uri,
          name: `recording-${Date.now()}.m4a`,
          mimeType: "audio/mp4",
        }
      );
      return;
    }

    const permission = await Audio.requestPermissionsAsync();
    if (!permission.granted) {
      Alert.alert("无法录音", "请允许麦克风权限。");
      return;
    }

    await Audio.setAudioModeAsync({ allowsRecordingIOS: true, playsInSilentModeIOS: true });
    const next = new Audio.Recording();
    await next.prepareToRecordAsync(Audio.RecordingOptionsPresets.HIGH_QUALITY);
    await next.startAsync();
    setRecording(next);
  }

  async function submitOrQueue(
    request: () => Promise<unknown>,
    queueItem: Parameters<typeof enqueueCapture>[0]
  ) {
    setBusy(true);
    try {
      await request();
      Alert.alert("已发送", "内容已进入 Inbox。");
    } catch {
      await enqueueCapture(queueItem);
      await refreshQueue();
      Alert.alert("已离线保存", "恢复网络后会自动重试。");
    } finally {
      setBusy(false);
    }
  }

  async function deactivateCurrentDevice() {
    if (!clientId) return;
    setBusy(true);
    try {
      await deactivateDevice(clientId);
      await clearDeviceTokens();
      Alert.alert("设备已停用", "此设备的采集凭证已失效。重新打开应用会再次绑定。");
    } catch {
      Alert.alert("停用失败", "请稍后重试。");
    } finally {
      setBusy(false);
    }
  }

  async function pairCurrentDevice() {
    const code = pairingCode.trim();
    if (!code || !clientId) return;

    setBusy(true);
    try {
      const pushToken = await registerPushToken().catch(() => undefined);
      const binding = await pairDevice({
        code,
        clientId,
        deviceName: Platform.OS === "ios" ? "iPhone" : "Android",
        platform: Platform.OS,
        pushToken,
      });
      await setDeviceAccessToken(binding.deviceAccessToken);
      await setDeviceRefreshToken(binding.refreshToken);
      setPairingCode("");
      Alert.alert("配对成功", "此设备已可直接发送到 Inbox。");
    } catch {
      Alert.alert("配对失败", "请检查配对码是否正确或已过期。");
    } finally {
      setBusy(false);
    }
  }

  return (
    <SafeAreaView style={styles.screen}>
      <StatusBar style="light" />
      <View style={styles.container}>
        <View style={styles.header}>
          <Text style={styles.title}>Memorix</Text>
          <Text style={styles.subtitle}>发送到知识库 Inbox</Text>
        </View>

        <View style={styles.segment}>
          {(["text", "url", "file", "audio"] as Mode[]).map((item) => (
            <Pressable
              key={item}
              style={[styles.segmentButton, mode === item && styles.segmentButtonActive]}
              onPress={() => setMode(item)}
            >
              <Text style={[styles.segmentText, mode === item && styles.segmentTextActive]}>
                {item === "text" ? "文字" : item === "url" ? "链接" : item === "audio" ? "录音" : "文件"}
              </Text>
            </Pressable>
          ))}
        </View>

        <View style={styles.pairingBox}>
          <TextInput
            value={pairingCode}
            onChangeText={setPairingCode}
            placeholder="输入桌面端配对码"
            placeholderTextColor="#7b8496"
            keyboardType="number-pad"
            maxLength={6}
            style={[styles.input, styles.pairingInput]}
          />
          <Pressable disabled={busy || !clientId || pairingCode.trim().length < 6} style={styles.pairingButton} onPress={pairCurrentDevice}>
            <Text style={styles.pairingText}>配对</Text>
          </Pressable>
        </View>

        <View style={styles.panel}>
          <Text style={styles.panelTitle}>{modeLabel}</Text>
          {mode === "text" && (
            <>
              <TextInput
                value={text}
                onChangeText={setText}
                multiline
                placeholder="写下想法、摘录或灵感"
                placeholderTextColor="#7b8496"
                style={[styles.input, styles.textArea]}
              />
              <Pressable disabled={busy || !clientId} style={styles.primaryButton} onPress={submitText}>
                <Text style={styles.primaryText}>发送</Text>
              </Pressable>
            </>
          )}
          {mode === "url" && (
            <>
              <TextInput value={url} onChangeText={setUrl} placeholder="https://..." placeholderTextColor="#7b8496" style={styles.input} />
              <TextInput value={title} onChangeText={setTitle} placeholder="标题，可选" placeholderTextColor="#7b8496" style={styles.input} />
              <Pressable disabled={busy || !clientId} style={styles.primaryButton} onPress={submitUrl}>
                <Text style={styles.primaryText}>发送</Text>
              </Pressable>
            </>
          )}
          {mode === "file" && (
            <Pressable disabled={busy || !clientId} style={styles.primaryButton} onPress={pickFile}>
              <Text style={styles.primaryText}>选择文件</Text>
            </Pressable>
          )}
          {mode === "audio" && (
            <Pressable disabled={busy || !clientId} style={styles.primaryButton} onPress={toggleRecording}>
              <Text style={styles.primaryText}>{recording ? "停止并发送" : "开始录音"}</Text>
            </Pressable>
          )}
        </View>

        <View style={styles.footer}>
          <Text style={styles.footerText}>设备 {clientId ? "已绑定" : "绑定中"}</Text>
          <Text style={styles.footerText}>离线队列 {pendingCount}</Text>
          {failedCount > 0 && (
            <Text style={styles.failureText}>失败 {failedCount}</Text>
          )}
          <Pressable onPress={() => flushQueue(true).then(refreshQueue)}>
            <Text style={styles.syncText}>同步</Text>
          </Pressable>
          <Pressable onPress={deactivateCurrentDevice}>
            <Text style={styles.dangerText}>停用</Text>
          </Pressable>
        </View>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1,
    backgroundColor: "#0f172a",
  },
  container: {
    flex: 1,
    padding: 20,
    gap: 18,
  },
  header: {
    paddingTop: 20,
  },
  title: {
    color: "#f8fafc",
    fontSize: 32,
    fontWeight: "700",
  },
  subtitle: {
    color: "#94a3b8",
    marginTop: 4,
  },
  segment: {
    flexDirection: "row",
    gap: 8,
  },
  segmentButton: {
    flex: 1,
    height: 42,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#1e293b",
  },
  segmentButtonActive: {
    backgroundColor: "#f8fafc",
  },
  segmentText: {
    color: "#cbd5e1",
    fontWeight: "600",
  },
  segmentTextActive: {
    color: "#0f172a",
  },
  panel: {
    gap: 12,
  },
  pairingBox: {
    flexDirection: "row",
    gap: 10,
    alignItems: "center",
  },
  pairingInput: {
    flex: 1,
  },
  pairingButton: {
    height: 48,
    minWidth: 76,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#e2e8f0",
  },
  pairingText: {
    color: "#0f172a",
    fontWeight: "700",
  },
  panelTitle: {
    color: "#f8fafc",
    fontSize: 18,
    fontWeight: "700",
  },
  input: {
    minHeight: 48,
    borderRadius: 8,
    backgroundColor: "#1e293b",
    color: "#f8fafc",
    paddingHorizontal: 14,
    paddingVertical: 12,
  },
  textArea: {
    minHeight: 160,
    textAlignVertical: "top",
  },
  primaryButton: {
    height: 50,
    borderRadius: 8,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#38bdf8",
  },
  primaryText: {
    color: "#082f49",
    fontWeight: "700",
  },
  footer: {
    marginTop: "auto",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  footerText: {
    color: "#94a3b8",
    fontSize: 12,
  },
  syncText: {
    color: "#7dd3fc",
    fontWeight: "700",
  },
  dangerText: {
    color: "#fda4af",
    fontWeight: "700",
  },
  failureText: {
    color: "#fbbf24",
    fontSize: 12,
  },
});
