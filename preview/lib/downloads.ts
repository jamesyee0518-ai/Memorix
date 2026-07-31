export type DesktopPlatform = "macos" | "windows" | "other";

export const desktopDownloads = {
  version: "0.1.3",
  macos: {
    platform: "macos",
    architecture: "Apple Silicon / ARM64",
    format: "ZIP",
    fileName: "memorix-macos-arm64.zip",
    url:
      process.env.NEXT_PUBLIC_MEMORIX_MAC_DOWNLOAD_URL ??
      "/downloads/memorix-macos-arm64.zip",
  },
  windows: {
    platform: "windows",
    architecture: "Windows x64",
    format: "ZIP",
    fileName: "memorix-windows-x64.zip",
    url:
      process.env.NEXT_PUBLIC_MEMORIX_WINDOWS_DOWNLOAD_URL ??
      "/downloads/memorix-windows-x64.zip",
  },
} as const;

export function detectDesktopPlatform(): DesktopPlatform {
  if (typeof navigator === "undefined") return "other";
  const nav = navigator as Navigator & { userAgentData?: { platform?: string } };
  const source = [nav.userAgentData?.platform, navigator.platform, navigator.userAgent]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();

  if (/macintosh|macintel|mac os|macos/.test(source)) return "macos";
  if (/windows|win32|win64/.test(source)) return "windows";
  return "other";
}
