# Memorix Desktop

Tauri desktop shell for the Memorix local-first workspace.

Current scope:

- starts the local ASP.NET API and Next.js web workspace during `tauri dev`
- opens the desktop window against `http://localhost:3000`
- keeps the existing web/API architecture intact
- prepares production resources with `npm run prepare:bundle`
- production shell attempts to launch bundled API and Next standalone runtime from Tauri resources
- production web runtime prefers a bundled Node runtime from `resources/node`, then falls back to system `node`
- startup waits for both the API and web runtime to accept connections before continuing
- the setup and workspace pages use the native system directory picker for Vault selection
- the workspace page can open the active Vault in Finder, Explorer, or the Linux file manager
- Tauri bundle generation is enabled
- packaged desktop mode uses an app-local SQLite metadata database and does not require PostgreSQL
- bundle preparation uses a cross-platform Node script and auto-detects `osx-*`, `win-*`, or `linux-*` runtime IDs
- Windows starts the web runtime through `cmd.exe`; macOS and Linux use the executable shell launcher
- API and Web logs are persisted under the platform application-data directory

Run:

```bash
npm install
npm run dev
```

Prepare bundle resources:

```bash
npm run prepare:bundle
```

Prepare bundle resources with a bundled Node runtime:

```bash
MEMORIX_NODE_RUNTIME_DIR=/path/to/node-runtime npm run prepare:bundle
```

`MEMORIX_NODE_RUNTIME_DIR` should point to the unpacked runtime root for the current platform. The launcher checks `bin/node`, `node`, and `node.exe`.

For a compact bundle, provide only the platform Node executable:

```bash
MEMORIX_NODE_BINARY=/path/to/node npm run prepare:bundle
```

The executable is copied to `resources/node/bin/node` and does not include npm or development headers.

On macOS, Homebrew may provide a dynamically linked Node binary that cannot be copied by itself. The build rejects that binary; set `MEMORIX_NODE_BINARY` to an official standalone Node executable or provide a complete `MEMORIX_NODE_RUNTIME_DIR`.

Prepare a platform-specific API publish:

```bash
MEMORIX_DESKTOP_RUNTIME_ID=osx-arm64 MEMORIX_DESKTOP_SELF_CONTAINED=true npm run prepare:bundle
```

After the runtime packages have been restored once, repeatable offline builds can add `MEMORIX_DESKTOP_NO_RESTORE=true`.

Build installer:

```bash
npm run build:bundle
```

Verified macOS ARM64 build:

```bash
MEMORIX_NODE_BINARY=/path/to/node \
MEMORIX_DESKTOP_RUNTIME_ID=osx-arm64 \
MEMORIX_DESKTOP_SELF_CONTAINED=true \
npm run build
```

The verified output is `target/release/bundle/dmg/Memorix_0.1.0_aarch64.dmg`.

Native bundle outputs:

```text
macOS:  target/release/bundle/dmg/*.dmg
Windows: target/release/bundle/msi/*.msi and nsis/*.exe
Linux: target/release/bundle/deb/*.deb, appimage/*.AppImage, and rpm/*.rpm
```

`.github/workflows/desktop-build.yml` builds each platform on its native CI runner. The bundled Node executable defaults to the Node process running the build, so no platform-specific path is required in CI.

The Windows x64 and Linux x64 self-contained API publishes have been cross-checked from macOS. Native MSI/NSIS, DEB/AppImage/RPM generation and installation still run on their corresponding CI operating systems.

Still pending for release-quality packaging:

- supply and verify real Node runtime artifacts for each release platform
- final branded icon, signing, notarization, updater
- tray menu and local service health controls
- Apple Developer ID signing, notarization, and Windows/Linux installer verification
