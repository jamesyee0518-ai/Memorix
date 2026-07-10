import { spawnSync } from "node:child_process";
import {
  chmodSync,
  cpSync,
  existsSync,
  mkdirSync,
  rmSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const desktopDir = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const rootDir = resolve(desktopDir, "..");
const resourceDir = join(desktopDir, "src-tauri", "resources");
const apiOut = join(resourceDir, "api");
const webOut = join(resourceDir, "web");
const nodeOut = join(resourceDir, "node");
const npm = process.platform === "win32" ? "npm.cmd" : "npm";

function run(command, args, cwd = rootDir) {
  const result = spawnSync(command, args, {
    cwd,
    env: process.env,
    stdio: "inherit",
    shell: false,
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${command} exited with code ${result.status}`);
  }
}

function runtimeId() {
  if (process.env.MEMORIX_DESKTOP_RUNTIME_ID) {
    return process.env.MEMORIX_DESKTOP_RUNTIME_ID;
  }
  const arch = process.arch === "arm64" ? "arm64" : "x64";
  if (process.platform === "darwin") return `osx-${arch}`;
  if (process.platform === "win32") return `win-${arch}`;
  return `linux-${arch}`;
}

function resetDirectory(path) {
  rmSync(path, { recursive: true, force: true });
  mkdirSync(path, { recursive: true });
}

function copyBundledNode() {
  resetDirectory(nodeOut);
  const runtimeDir = process.env.MEMORIX_NODE_RUNTIME_DIR;
  if (runtimeDir) {
    if (!existsSync(runtimeDir)) {
      throw new Error(`MEMORIX_NODE_RUNTIME_DIR does not exist: ${runtimeDir}`);
    }
    cpSync(runtimeDir, nodeOut, { recursive: true });
    return;
  }

  if (process.env.MEMORIX_SKIP_NODE_BUNDLE === "true") {
    console.warn("Node bundling is disabled; the app will fall back to system node.");
    return;
  }

  const source = process.env.MEMORIX_NODE_BINARY || process.execPath;
  if (!existsSync(source)) {
    throw new Error(`Node executable does not exist: ${source}`);
  }
  if (process.platform === "darwin") {
    const linked = spawnSync("otool", ["-L", source], { encoding: "utf8" });
    const nonPortable = (linked.stdout || "")
      .split("\n")
      .slice(1)
      .some((line) => {
        const dependency = line.trim();
        return (
          dependency.startsWith("@rpath/") ||
          dependency.startsWith("/opt/homebrew/") ||
          dependency.startsWith("/usr/local/opt/")
        );
      });
    if (nonPortable) {
      throw new Error(
        "The selected Node executable depends on package-manager libraries. " +
          "Set MEMORIX_NODE_BINARY to an official standalone Node executable."
      );
    }
  }
  const destination =
    process.platform === "win32"
      ? join(nodeOut, "node.exe")
      : join(nodeOut, "bin", "node");
  mkdirSync(dirname(destination), { recursive: true });
  cpSync(source, destination);
  if (process.platform !== "win32") chmodSync(destination, 0o755);

  const license = resolve(dirname(source), "..", "LICENSE");
  if (existsSync(license)) cpSync(license, join(nodeOut, "LICENSE"));
}

resetDirectory(apiOut);
resetDirectory(webOut);
mkdirSync(join(resourceDir, "bin"), { recursive: true });

run(npm, ["--prefix", join(rootDir, "web"), "run", "build"]);

const publishArgs = [
  "publish",
  join(rootDir, "src", "KnowledgeEngine.Api", "KnowledgeEngine.Api.csproj"),
  "-c",
  "Release",
  "-o",
  apiOut,
  "--self-contained",
  process.env.MEMORIX_DESKTOP_SELF_CONTAINED ?? "true",
  "-r",
  runtimeId(),
];
if (process.env.MEMORIX_DESKTOP_NO_RESTORE === "true") {
  publishArgs.push("--no-restore");
}
run("dotnet", publishArgs);

cpSync(join(rootDir, "web", ".next", "standalone"), webOut, {
  recursive: true,
});
mkdirSync(join(webOut, ".next"), { recursive: true });
cpSync(join(rootDir, "web", ".next", "static"), join(webOut, ".next", "static"), {
  recursive: true,
});
const publicDir = join(rootDir, "web", "public");
if (existsSync(publicDir)) {
  cpSync(publicDir, join(webOut, "public"), { recursive: true });
}

if (process.platform !== "win32") {
  chmodSync(join(resourceDir, "bin", "memorix-web"), 0o755);
}
copyBundledNode();

console.log(`Desktop bundle resources prepared for ${runtimeId()} in ${resourceDir}`);
