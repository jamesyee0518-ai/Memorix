export type DesktopPlatform = "macos" | "windows" | "other";

export const desktopDownloads = {
  version: "0.1.2",
  macos: {
    platform: "macos",
    architecture: "Apple Silicon / ARM64",
    format: "DMG",
    fileName: "Memorix_0.1.2_aarch64.dmg",
    url: process.env.NEXT_PUBLIC_MEMORIX_MAC_DOWNLOAD_URL ?? "",
  },
  windows: {
    platform: "windows",
    architecture: "Windows x64",
    format: "EXE",
    fileName: "Memorix_0.1.2_x64-setup.exe",
    url: process.env.NEXT_PUBLIC_MEMORIX_WINDOWS_DOWNLOAD_URL ?? "",
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
