import { createHash } from "node:crypto";
import {
  copyFileSync,
  mkdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const version = "0.1.3";
const root = dirname(dirname(fileURLToPath(import.meta.url)));
const output = join(root, "artifacts", `memorix.hiqer.top-release-v${version}`);

const releaseFiles = [
  {
    source: "preview/preview.html",
    target: "preview/preview.html",
    role: "preview",
  },
  {
    source: "deploy/memorix.hiqer.top/web.config",
    target: "web.config",
    role: "iis-config",
  },
  {
    source: "deploy/memorix.hiqer.top/nginx.conf",
    target: "nginx.conf",
    role: "nginx-config",
  },
  {
    source: "deploy/memorix.hiqer.top/README.md",
    target: "README.md",
    role: "deployment-guide",
  },
  {
    source: "downloads/memorix-macos-arm64.zip",
    target: "downloads/memorix-macos-arm64.zip",
    role: "macos-installer",
  },
  {
    source: "downloads/memorix-windows-x64.zip",
    target: "downloads/memorix-windows-x64.zip",
    role: "windows-installer",
  },
];

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

rmSync(output, { recursive: true, force: true });
mkdirSync(output, { recursive: true });

const manifestFiles = releaseFiles.map(({ source, target, role }) => {
  const sourcePath = join(root, source);
  const targetPath = join(output, target);
  mkdirSync(dirname(targetPath), { recursive: true });
  copyFileSync(sourcePath, targetPath);

  return {
    path: target,
    role,
    bytes: statSync(targetPath).size,
    sha256: sha256(targetPath),
  };
});

const manifest = {
  product: "Memorix",
  version,
  domain: "memorix.hiqer.top",
  defaultDocument: "preview/preview.html",
  generatedAt: new Date().toISOString(),
  files: manifestFiles,
};

writeFileSync(
  join(output, "release-manifest.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
);

console.log(output);
