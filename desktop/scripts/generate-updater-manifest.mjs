import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { basename, dirname, join, resolve } from "node:path";

function argument(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : fallback;
}

async function walk(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) files.push(...(await walk(path)));
    else files.push(path);
  }
  return files;
}

function findSignedArtifact(files, matcher) {
  const signature = files.find((file) => file.endsWith(".sig") && matcher(file.slice(0, -4)));
  if (!signature) return null;
  return { artifact: signature.slice(0, -4), signature };
}

const input = resolve(argument("input", "release-input"));
const output = resolve(argument("output", "desktop-updates/stable/latest.json"));
const version = argument("version", process.env.RELEASE_VERSION)?.replace(/^v/, "");
const baseUrl = (
  argument("base-url", "https://memorix.hiqer.top/desktop-updates/releases") || ""
).replace(/\/$/, "");
const notes = argument("notes", `Memorix ${version}`);

if (!version) throw new Error("--version or RELEASE_VERSION is required");

const files = await walk(input);
const mac = findSignedArtifact(files, (file) => file.endsWith(".app.tar.gz"));
const windows = findSignedArtifact(
  files,
  (file) => /setup\.exe$/i.test(file) || /\.msi$/i.test(file)
);

if (!mac) throw new Error("macOS ARM64 updater artifact and .sig were not found");
if (!windows) throw new Error("Windows x64 updater artifact and .sig were not found");

async function platformEntry(pair) {
  return {
    signature: (await readFile(pair.signature, "utf8")).trim(),
    url: `${baseUrl}/${version}/${encodeURIComponent(basename(pair.artifact))}`,
  };
}

const manifest = {
  version,
  notes,
  pub_date: new Date().toISOString(),
  platforms: {
    "darwin-aarch64": await platformEntry(mac),
    "windows-x86_64": await platformEntry(windows),
  },
};

await mkdir(dirname(output), { recursive: true });
await writeFile(output, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
console.log(`Updater manifest written to ${output}`);
