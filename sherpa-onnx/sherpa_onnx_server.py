#!/usr/bin/env python3
"""
Memorix sherpa-onnx STT 微服务
提供与 FunAsrAsrProvider 兼容的 HTTP API

端点:
  POST /api/asr       — 上传音频文件进行离线 STT
  GET  /health        — 健康检查
  GET  /api/models    — 列出已加载模型

启动:
  python3 sherpa_onnx_server.py --model paraformer --port 8001
  python3 sherpa_onnx_server.py --model sensevoice --port 8001
"""

import argparse
import io
import os
import time
import wave
import tempfile
import subprocess
from pathlib import Path
from typing import Optional

import numpy as np
import sherpa_onnx
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import JSONResponse
import uvicorn

# ── 路径配置 ──
BASE_DIR = Path(__file__).parent.resolve()
PARA_DIR = BASE_DIR / "asr" / "sherpa-onnx-paraformer-zh-2023-09-14"
SENSE_DIR = BASE_DIR / "asr" / "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17"

app = FastAPI(title="Memorix sherpa-onnx STT Service", version="1.0.0")

# 全局识别器
recognizer = None
model_info = {}


def load_paraformer():
    """加载 Paraformer int8 模型"""
    model_path = PARA_DIR / "model.int8.onnx"
    tokens_path = PARA_DIR / "tokens.txt"

    if not model_path.exists():
        raise FileNotFoundError(f"Paraformer 模型不存在: {model_path}")

    print(f"[INFO] 加载 Paraformer 模型: {model_path.name}")
    t0 = time.time()
    rec = sherpa_onnx.OfflineRecognizer.from_paraformer(
        paraformer=str(model_path),
        tokens=str(tokens_path),
        num_threads=2,
        sample_rate=16000,
        feature_dim=80,
        decoding_method='greedy_search',
        debug=False,
        provider='cpu'
    )
    init_time = time.time() - t0
    print(f"[OK] Paraformer 加载完成 ({init_time:.2f}s)")

    return rec, {
        "provider": "sherpa-onnx",
        "model": "paraformer-zh-int8",
        "model_file": model_path.name,
        "model_size_mb": round(model_path.stat().st_size / 1e6, 0),
        "init_time_s": round(init_time, 2),
        "supports_punctuation": False,
        "supports_itn": False,
        "languages": ["zh", "en"],
    }


def load_sensevoice():
    """加载 SenseVoice int8 模型"""
    model_path = SENSE_DIR / "model.int8.onnx"
    tokens_path = SENSE_DIR / "tokens.txt"

    if not model_path.exists():
        raise FileNotFoundError(f"SenseVoice 模型不存在: {model_path}")

    print(f"[INFO] 加载 SenseVoice 模型: {model_path.name}")
    t0 = time.time()
    rec = sherpa_onnx.OfflineRecognizer.from_sense_voice(
        model=str(model_path),
        tokens=str(tokens_path),
        num_threads=2,
        sample_rate=16000,
        feature_dim=80,
        decoding_method='greedy_search',
        debug=False,
        provider='cpu',
        language='auto',
        use_itn=True
    )
    init_time = time.time() - t0
    print(f"[OK] SenseVoice 加载完成 ({init_time:.2f}s)")

    return rec, {
        "provider": "sherpa-onnx",
        "model": "sense-voice-int8",
        "model_file": model_path.name,
        "model_size_mb": round(model_path.stat().st_size / 1e6, 0),
        "init_time_s": round(init_time, 2),
        "supports_punctuation": True,
        "supports_itn": True,
        "languages": ["zh", "en", "ja", "ko", "yue"],
    }


def normalize_audio(input_path: str, output_path: str) -> bool:
    """FFmpeg 标准化 → 16kHz mono pcm_s16le"""
    cmd = [
        "ffmpeg", "-y", "-i", input_path,
        "-ar", "16000", "-ac", "1",
        "-acodec", "pcm_s16le",
        output_path
    ]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
    return r.returncode == 0


def read_wav_samples(wav_path: str):
    """读取 WAV 文件返回 (sample_rate, float32_array)"""
    with wave.open(wav_path, 'rb') as wf:
        sample_rate = wf.getframerate()
        n_channels = wf.getnchannels()
        sampwidth = wf.getsampwidth()
        n_frames = wf.getnframes()
        raw_data = wf.readframes(n_frames)

    if sampwidth == 2:
        samples = np.frombuffer(raw_data, dtype=np.int16).astype(np.float32) / 32768.0
    elif sampwidth == 4:
        samples = np.frombuffer(raw_data, dtype=np.int32).astype(np.float32) / 2147483648.0
    elif sampwidth == 1:
        samples = (np.frombuffer(raw_data, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    else:
        raise ValueError(f"Unsupported sample width: {sampwidth}")

    if n_channels > 1:
        samples = samples[::n_channels]

    return sample_rate, samples


@app.on_event("startup")
async def startup_event():
    global recognizer, model_info
    model_type = os.environ.get("SHERPA_MODEL", "paraformer")

    if model_type == "sensevoice":
        recognizer, model_info = load_sensevoice()
    else:
        recognizer, model_info = load_paraformer()

    print(f"[INFO] 服务就绪 — 模型: {model_info['model']}")


@app.get("/health")
async def health_check():
    """健康检查端点"""
    return {
        "status": "healthy" if recognizer is not None else "unhealthy",
        "model": model_info.get("model", "unknown"),
        "provider": model_info.get("provider", "sherpa-onnx"),
    }


@app.get("/api/models")
async def list_models():
    """列出已加载模型信息"""
    return model_info


@app.post("/api/asr")
async def transcribe(
    audio: UploadFile = File(...),
    language: Optional[str] = Form(default=None),
    use_itn: Optional[str] = Form(default="true"),
):
    """
    离线语音识别端点
    兼容 FunAsrAsrProvider 的 /api/asr 接口格式

    参数:
      audio: 音频文件 (WAV, MP3, M4A, etc.)
      language: 语言代码 (可选, SenseVoice 忽略此参数)
      use_itn: 是否启用逆文本归一化 (仅 SenseVoice 有效)

    返回:
      JSON: { text, sentences: [{text, start, end}] }
    """
    if recognizer is None:
        raise HTTPException(status_code=503, detail="STT 模型未加载")

    # 保存上传的音频到临时文件
    audio_bytes = await audio.read()
    if not audio_bytes:
        raise HTTPException(status_code=400, detail="音频文件为空")

    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_input:
        tmp_input.write(audio_bytes)
        tmp_input_path = tmp_input.name

    tmp_norm_path = tmp_input_path.replace(".wav", "_norm.wav")

    try:
        # Step 1: FFmpeg 标准化
        t0 = time.time()
        ok = normalize_audio(tmp_input_path, tmp_norm_path)
        ff_time = time.time() - t0

        if not ok or not os.path.exists(tmp_norm_path):
            # 标准化失败, 尝试使用原始文件
            tmp_norm_path = tmp_input_path
            ff_time = 0

        # Step 2: 读取标准化音频
        sr, samples = read_wav_samples(tmp_norm_path)
        duration = len(samples) / sr

        # Step 3: STT 识别
        t0 = time.time()
        stream = recognizer.create_stream()
        stream.accept_waveform(sr, samples)
        recognizer.decode_stream(stream)
        text = stream.result.text.strip()
        stt_time = time.time() - t0
        rtf = stt_time / duration if duration > 0 else 0

        # 构建返回结果 (兼容 FunAsrAsrProvider 格式)
        sentences = []
        if text:
            sentences.append({
                "text": text,
                "start": 0,
                "end": int(duration * 1000),
            })

        result = {
            "text": text,
            "sentences": sentences,
            "key": "",
            "metadata": {
                "provider": model_info.get("provider", "sherpa-onnx"),
                "model": model_info.get("model", "unknown"),
                "duration_s": round(duration, 2),
                "stt_time_s": round(stt_time, 3),
                "rtf": round(rtf, 3),
                "ffmpeg_time_s": round(ff_time, 3),
                "sample_rate": sr,
                "samples": len(samples),
            }
        }

        print(f"[ASR] {audio.filename} → {duration:.1f}s audio, "
              f"STT={stt_time:.2f}s (RTF={rtf:.3f}), "
              f"text={text[:60]}{'...' if len(text) > 60 else ''}")

        return JSONResponse(content=result)

    except Exception as e:
        print(f"[ERROR] ASR failed: {e}")
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=f"STT 识别失败: {str(e)}")

    finally:
        # 清理临时文件
        for path in [tmp_input_path, tmp_norm_path]:
            try:
                if os.path.exists(path):
                    os.unlink(path)
            except Exception:
                pass


@app.post("/api/asr/stream")
async def transcribe_stream():
    """
    流式 STT 端点 (预留接口)
    当前返回 501 Not Implemented
    """
    raise HTTPException(status_code=501, detail="流式 STT 尚未实现, 请使用 /api/asr 离线接口")


def main():
    parser = argparse.ArgumentParser(description="Memorix sherpa-onnx STT 微服务")
    parser.add_argument(
        "--model", type=str, default="paraformer",
        choices=["paraformer", "sensevoice"],
        help="选择 STT 模型 (默认: paraformer)"
    )
    parser.add_argument(
        "--port", type=int, default=8001,
        help="服务端口 (默认: 8001)"
    )
    parser.add_argument(
        "--host", type=str, default="0.0.0.0",
        help="监听地址 (默认: 0.0.0.0)"
    )
    args = parser.parse_args()

    # 设置环境变量供 startup 事件使用
    os.environ["SHERPA_MODEL"] = args.model

    print(f"\n{'='*60}")
    print(f"  Memorix sherpa-onnx STT 微服务")
    print(f"  模型: {args.model}")
    print(f"  地址: http://{args.host}:{args.port}")
    print(f"  端点: POST /api/asr, GET /health, GET /api/models")
    print(f"{'='*60}\n")

    uvicorn.run(app, host=args.host, port=args.port, log_level="info")


if __name__ == "__main__":
    main()
