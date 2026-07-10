# Memorix Mobile

Expo/React Native mobile client for direct Inbox capture.

Implemented in this scaffold:

- device binding against `/api/mobile/devices/bind`
- text, URL, file, and audio upload to `/api/mobile/capture/*`
- offline queue with network-recovery sync, exponential backoff, error tracking, and concurrent-flush protection
- push-token registration hook for later notification delivery
- built-in recording flow foundation through `expo-av`

The checked-in dependency lockfile can be verified with:

```bash
npm run typecheck
```

Run in a network-enabled development environment:

```bash
npm install
npm run ios
npm run android
```

Set the API endpoint in `app.json` under `expo.extra.apiBaseUrl`.
