export function isDesktopApp(): boolean {
  return (
    typeof window !== "undefined" &&
    "__TAURI_INTERNALS__" in window
  );
}

export async function selectDesktopDirectory(
  defaultPath?: string
): Promise<string | null> {
  if (!isDesktopApp()) return null;

  const { open } = await import("@tauri-apps/plugin-dialog");
  const selected = await open({
    directory: true,
    multiple: false,
    defaultPath: defaultPath || undefined,
  });

  return typeof selected === "string" ? selected : null;
}

export async function openDesktopDirectory(path: string): Promise<void> {
  if (!isDesktopApp()) {
    throw new Error("当前环境不是 Memorix 桌面应用");
  }

  const { invoke } = await import("@tauri-apps/api/core");
  await invoke("open_directory", { path });
}

export async function openExternalUrl(url: string): Promise<void> {
  if (isDesktopApp()) {
    const { invoke } = await import("@tauri-apps/api/core");
    await invoke("open_external_url", { url });
    return;
  }

  const opened = window.open(url, "_blank", "noopener,noreferrer");
  if (!opened) {
    throw new Error("浏览器阻止了登录窗口，请允许弹出窗口后重试");
  }
}
