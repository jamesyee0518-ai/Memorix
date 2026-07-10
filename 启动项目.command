#!/bin/zsh

set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
WEB_DIR="$PROJECT_DIR/web"
API_PROJECT="$PROJECT_DIR/src/KnowledgeEngine.Api/KnowledgeEngine.Api.csproj"
LOG_DIR="$PROJECT_DIR/.logs"
API_LOG="$LOG_DIR/api.log"

API_PORT=9101
WEB_PORT=3000
API_PID=""
OPEN_BROWSER_PID=""

print_title() {
  echo ""
  echo "========================================"
  echo "$1"
  echo "========================================"
}

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

cleanup() {
  if [ -n "$OPEN_BROWSER_PID" ]; then
    kill "$OPEN_BROWSER_PID" >/dev/null 2>&1 || true
  fi

  if [ -n "$API_PID" ]; then
    echo ""
    echo "正在停止后端 API..."
    kill "$API_PID" >/dev/null 2>&1 || true
  fi
}

trap cleanup EXIT INT TERM

ensure_tools() {
  if ! command_exists node || ! command_exists npm; then
    echo "请先安装 Node.js，然后再次双击本脚本。"
    echo "推荐安装地址：https://nodejs.org/"
    read -r "?按回车键退出..."
    exit 1
  fi

  if ! command_exists dotnet; then
    echo "请先安装 .NET SDK，然后再次双击本脚本。"
    echo "推荐安装地址：https://dotnet.microsoft.com/download"
    read -r "?按回车键退出..."
    exit 1
  fi

  if ! command_exists docker; then
    echo "未检测到 Docker。后端需要 PostgreSQL、Redis、MinIO 才能运行。"
    echo "请先安装并打开 Docker Desktop。"
    read -r "?按回车键退出..."
    exit 1
  fi
}

start_docker_dependencies() {
  if ! docker info >/dev/null 2>&1; then
    echo "Docker Desktop 尚未运行。"
    echo "我会尝试打开 Docker Desktop，请等它启动完成后重新双击本脚本。"
    open -a Docker >/dev/null 2>&1 || true
    read -r "?Docker Desktop 启动完成后，按回车键退出，然后重新双击本脚本..."
    exit 1
  fi

  print_title "启动本地依赖服务"
  docker compose up -d
  wait_for_postgres
}

wait_for_postgres() {
  print_title "等待数据库就绪"

  local attempt=1
  local max_attempts=60

  while [ "$attempt" -le "$max_attempts" ]; do
    if docker compose exec -T postgres pg_isready -U ke_user -d knowledge_engine >/dev/null 2>&1; then
      echo "数据库已就绪。"
      return 0
    fi

    printf "等待中... %d/%d\r" "$attempt" "$max_attempts"
    sleep 2
    attempt=$((attempt + 1))
  done

  echo ""
  echo "数据库暂未就绪，后端 API 无法启动。"
  read -r "?按回车键退出..."
  exit 1
}

ensure_web_dependencies() {
  if [ ! -d "$WEB_DIR" ]; then
    echo "找不到主应用目录：$WEB_DIR"
    exit 1
  fi

  if [ ! -d "$WEB_DIR/node_modules" ]; then
    print_title "正在安装主应用依赖"
    cd "$WEB_DIR"
    npm install
  fi
}

start_api() {
  if [ ! -f "$API_PROJECT" ]; then
    echo "找不到后端 API 项目：$API_PROJECT"
    exit 1
  fi

  mkdir -p "$LOG_DIR"

  if curl -fsS "http://localhost:$API_PORT/health" >/dev/null 2>&1; then
    echo "后端 API 已在运行：http://localhost:$API_PORT"
    return 0
  fi

  print_title "启动后端 API"
  echo "后端日志：$API_LOG"
  cd "$PROJECT_DIR"
  dotnet run --project "$API_PROJECT" --launch-profile http > "$API_LOG" 2>&1 &
  API_PID=$!
}

wait_for_api() {
  print_title "等待后端 API 就绪"

  local attempt=1
  local max_attempts=90

  while [ "$attempt" -le "$max_attempts" ]; do
    if curl -fsS "http://localhost:$API_PORT/health" >/dev/null 2>&1; then
      echo "后端 API 已就绪。"
      return 0
    fi

    if [ -n "$API_PID" ] && ! kill -0 "$API_PID" >/dev/null 2>&1; then
      echo ""
      echo "后端 API 启动失败。最近日志："
      tail -n 40 "$API_LOG" 2>/dev/null || true
      read -r "?按回车键退出..."
      exit 1
    fi

    printf "等待中... %d/%d\r" "$attempt" "$max_attempts"
    sleep 2
    attempt=$((attempt + 1))
  done

  echo ""
  echo "后端 API 暂未就绪。最近日志："
  tail -n 40 "$API_LOG" 2>/dev/null || true
  read -r "?按回车键退出..."
  exit 1
}

open_browser_after_web_starts() {
  (
    sleep 5
    open "http://localhost:$WEB_PORT" >/dev/null 2>&1 || true
  ) &
  OPEN_BROWSER_PID=$!
}

start_web() {
  print_title "启动主应用"
  echo "主应用：http://localhost:$WEB_PORT"
  echo "关闭这个窗口即可停止主应用和本次启动的后端 API。"
  echo ""
  cd "$WEB_DIR"
  open_browser_after_web_starts
  npm run dev -- --port "$WEB_PORT"
}

print_title "Memorix 一键启动"
cd "$PROJECT_DIR"

ensure_tools
start_docker_dependencies
ensure_web_dependencies
start_api
wait_for_api
start_web
