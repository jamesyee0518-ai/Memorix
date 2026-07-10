#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PIDS=()

cleanup() {
  for pid in "${PIDS[@]:-}"; do
    if kill -0 "$pid" >/dev/null 2>&1; then
      kill "$pid" >/dev/null 2>&1 || true
    fi
  done
}
trap cleanup EXIT INT TERM

is_up() {
  local url="$1"
  curl -fsS "$url" >/dev/null 2>&1
}

if ! is_up "http://localhost:9101/api/runtime/health"; then
  dotnet run \
    --project "$ROOT_DIR/src/KnowledgeEngine.Api/KnowledgeEngine.Api.csproj" \
    --urls "http://localhost:9101" &
  PIDS+=("$!")
fi

if ! is_up "http://localhost:3000"; then
  npm --prefix "$ROOT_DIR/web" run dev &
  PIDS+=("$!")
fi

while true; do
  sleep 3600
done
