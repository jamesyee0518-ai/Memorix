import { writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const publicKey = process.env.MEMORIX_UPDATER_PUBLIC_KEY?.trim();
if (!publicKey) {
  throw new Error("MEMORIX_UPDATER_PUBLIC_KEY is required for update builds");
}

const endpoint =
  process.env.MEMORIX_UPDATER_ENDPOINT?.trim() ||
  "https://memorix.hiqer.top/desktop-updates/stable/latest.json";

const config = {
  bundle: {
    createUpdaterArtifacts: true,
  },
  plugins: {
    updater: {
      pubkey: publicKey,
      endpoints: [endpoint],
      windows: {
        installMode: "passive",
      },
    },
  },
};

const output = resolve("src-tauri/tauri.release.conf.json");
await writeFile(output, `${JSON.stringify(config, null, 2)}\n`, "utf8");
console.log(`Updater release configuration written to ${output}`);
