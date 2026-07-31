import { readFile } from "node:fs/promises";

const packageJson = JSON.parse(await readFile("package.json", "utf8"));
const tauriConfig = JSON.parse(await readFile("src-tauri/tauri.conf.json", "utf8"));
const cargoToml = await readFile("src-tauri/Cargo.toml", "utf8");
const cargoVersion = cargoToml.match(
  /^\[package\][\s\S]*?^version\s*=\s*"([^"]+)"/m
)?.[1];

const versions = {
  "desktop/package.json": packageJson.version,
  "desktop/src-tauri/tauri.conf.json": tauriConfig.version,
  "desktop/src-tauri/Cargo.toml": cargoVersion,
};
const unique = new Set(Object.values(versions));
if (unique.size !== 1 || unique.has(undefined)) {
  throw new Error(
    `Desktop version mismatch: ${Object.entries(versions)
      .map(([file, version]) => `${file}=${version ?? "missing"}`)
      .join(", ")}`
  );
}

const expected = process.env.RELEASE_VERSION?.replace(/^v/, "");
if (expected && expected !== tauriConfig.version) {
  throw new Error(
    `Release tag ${expected} does not match desktop version ${tauriConfig.version}`
  );
}

console.log(`Desktop version ${tauriConfig.version} is consistent`);
