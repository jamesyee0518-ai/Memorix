"use client";

import { useEffect, useState } from "react";
import { Apple, Download, MonitorDown, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { desktopDownloads, detectDesktopPlatform, DesktopPlatform } from "@/lib/downloads";

type Lang = "zh" | "en";

export function DownloadButton({ lang, className }: { lang: Lang; className?: string }) {
  const [platform, setPlatform] = useState<DesktopPlatform>("other");
  const [detected, setDetected] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setPlatform(detectDesktopPlatform());
    setDetected(true);
  }, []);

  useEffect(() => {
    if (!open) return;
    const close = (event: KeyboardEvent) => event.key === "Escape" && setOpen(false);
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [open]);

  const item = platform === "macos" || platform === "windows" ? desktopDownloads[platform] : null;
  const label = !detected || !item
    ? (lang === "zh" ? "下载客户端" : "Download App")
    : platform === "macos"
      ? (lang === "zh" ? "下载 Mac 版" : "Download for Mac")
      : (lang === "zh" ? "下载 Windows 版" : "Download for Windows");
  const Icon = platform === "macos" ? Apple : MonitorDown;

  const base = cn(
    "inline-flex h-11 items-center justify-center gap-2 rounded-md bg-cyan-300 px-5 text-sm font-medium text-slate-950 shadow-glow transition hover:bg-cyan-200",
    className,
  );

  return (
    <>
      {item?.url ? (
        <a href={item.url} className={base} aria-label={`${label} · ${item.architecture}`}>
          <Icon size={18} aria-hidden="true" />{label}
        </a>
      ) : (
        <button type="button" className={base} onClick={() => setOpen(true)}>
          <Download size={18} aria-hidden="true" />{label}
        </button>
      )}

      {open && (
        <div className="fixed inset-0 z-[80] flex items-center justify-center bg-black/70 p-4" role="presentation" onMouseDown={() => setOpen(false)}>
          <div className="glass w-full max-w-2xl rounded-xl p-6" role="dialog" aria-modal="true" aria-labelledby="download-title" onMouseDown={(e) => e.stopPropagation()}>
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-sm font-medium text-cyan-300">MEMORIX DESKTOP</p>
                <h2 id="download-title" className="mt-2 text-2xl font-semibold text-white">{lang === "zh" ? "选择下载版本" : "Choose a download"}</h2>
              </div>
              <button autoFocus type="button" onClick={() => setOpen(false)} className="rounded-md border border-white/10 p-2 text-slate-300 hover:text-white" aria-label={lang === "zh" ? "关闭" : "Close"}><X size={18} /></button>
            </div>
            <div className="mt-6 grid gap-4 sm:grid-cols-2">
              {(["macos", "windows"] as const).map((key) => {
                const release = desktopDownloads[key];
                const available = Boolean(release.url);
                return (
                  <div key={key} className="rounded-lg border border-white/10 bg-slate-950/60 p-5">
                    <div className="flex items-center gap-3 text-white">{key === "macos" ? <Apple /> : <MonitorDown />}<strong>{key === "macos" ? "Memorix for macOS" : "Memorix for Windows"}</strong></div>
                    <dl className="mt-4 space-y-2 text-sm text-slate-300">
                      <div className="flex justify-between"><dt>{lang === "zh" ? "版本" : "Version"}</dt><dd>{desktopDownloads.version}</dd></div>
                      <div className="flex justify-between"><dt>{lang === "zh" ? "架构" : "Architecture"}</dt><dd>{release.architecture}</dd></div>
                      <div className="flex justify-between"><dt>{lang === "zh" ? "格式" : "Format"}</dt><dd>{release.format}</dd></div>
                    </dl>
                    {available ? <a href={release.url} className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-md bg-cyan-300 px-4 py-3 text-sm font-medium text-slate-950"><Download size={16} />{lang === "zh" ? `下载 ${release.format}` : `Download ${release.format}`}</a> : <p className="mt-5 rounded-md bg-white/5 px-4 py-3 text-center text-sm text-slate-400">{lang === "zh" ? "下载暂不可用" : "Download currently unavailable"}</p>}
                  </div>
                );
              })}
            </div>
            <p className="mt-5 text-sm leading-6 text-slate-400">{lang === "zh" ? "当前 macOS 版本仅支持 Apple Silicon（M1 及后续芯片）。下载地址由正式发布配置提供。" : "The current macOS build supports Apple Silicon only (M1 or later). Download URLs are supplied by the release configuration."}</p>
          </div>
        </div>
      )}
    </>
  );
}
