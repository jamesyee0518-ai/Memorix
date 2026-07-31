import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdtemp, mkdir, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { promisify } from "node:util";
import test from "node:test";

const execFileAsync = promisify(execFile);

test("generates a static manifest for macOS ARM64 and Windows x64", async () => {
  const root = await mkdtemp(join(tmpdir(), "memorix-updater-"));
  const input = join(root, "input");
  const output = join(root, "stable", "latest.json");
  await mkdir(join(input, "mac"), { recursive: true });
  await mkdir(join(input, "windows"), { recursive: true });

  const mac = join(input, "mac", "Memorix_0.2.0_aarch64.app.tar.gz");
  const windows = join(input, "windows", "Memorix_0.2.0_x64-setup.exe");
  await writeFile(mac, "mac-update");
  await writeFile(`${mac}.sig`, "mac-signature\n");
  await writeFile(windows, "windows-update");
  await writeFile(`${windows}.sig`, "windows-signature\n");

  await execFileAsync(process.execPath, [
    resolve("scripts/generate-updater-manifest.mjs"),
    "--input",
    input,
    "--output",
    output,
    "--version",
    "v0.2.0",
  ]);

  const manifest = JSON.parse(await readFile(output, "utf8"));
  assert.equal(manifest.version, "0.2.0");
  assert.equal(manifest.platforms["darwin-aarch64"].signature, "mac-signature");
  assert.equal(manifest.platforms["windows-x86_64"].signature, "windows-signature");
  assert.match(manifest.platforms["darwin-aarch64"].url, /\/0\.2\.0\/Memorix_0\.2\.0_aarch64/);
  assert.match(manifest.platforms["windows-x86_64"].url, /\/0\.2\.0\/Memorix_0\.2\.0_x64-setup\.exe$/);
});
