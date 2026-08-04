#!/bin/bash
# Memorix sherpa-onnx STT 微服务启动脚本
# 用法:
#   ./start_stt_service.sh              # 默认使用 Paraformer, 端口 8001
#   ./start_stt_service.sh sensevoice   # 使用 SenseVoice
#   ./start_stt_service.sh paraformer 8002  # 指定端口

MODEL=${1:-paraformer}
PORT=${2:-8001}
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=========================================="
echo "  Memorix sherpa-onnx STT 微服务"
echo "  模型: $MODEL"
echo "  端口: $PORT"
echo "=========================================="

cd "$SCRIPT_DIR"
exec python3 sherpa_onnx_server.py --model "$MODEL" --port "$PORT"
