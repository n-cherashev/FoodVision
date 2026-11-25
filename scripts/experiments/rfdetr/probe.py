"""Run the exported official RF-DETR Keypoint Preview ONNX on FoodVision frames."""

import argparse
import json
import math
import re
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort

ROOT = Path("/work")
MODEL = ROOT / ".playwright-mcp/rfdetr-keypoint/rfdetr-keypoint-preview.onnx"
THRESHOLD = 0.08
TRACE_ALPHA = 0.2
INPUT_SIZE = 576
MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)[:, None, None]
STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)[:, None, None]


def preprocess(path: Path) -> np.ndarray:
    bgr = np.fromfile(path, dtype=np.uint8).reshape(360, 640, 3)
    rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB).transpose(2, 0, 1).astype(np.float32) / 255
    resized = cv2.resize(rgb.transpose(1, 2, 0), (INPUT_SIZE, INPUT_SIZE), interpolation=cv2.INTER_LINEAR)
    return (resized.transpose(2, 0, 1)[None] - MEAN) / STD


def sigmoid(values: np.ndarray) -> np.ndarray:
    return 1 / (1 + np.exp(-values))


def flexed_knee(points: np.ndarray, hip: int, knee: int, ankle: int) -> bool:
    a, b, c = points[hip], points[knee], points[ankle]
    if min(a[2], b[2], c[2]) < 0.2:
        return False
    ab, cb = a[:2] - b[:2], c[:2] - b[:2]
    denominator = math.sqrt(float(np.dot(ab, ab) * np.dot(cb, cb)))
    if denominator < 1:
        return False
    angle = math.degrees(math.acos(float(np.clip(np.dot(ab, cb) / denominator, -1, 1))))
    return 55 <= angle <= 145


def decode(dets: np.ndarray, labels: np.ndarray, keypoints: np.ndarray) -> list[dict]:
    flattened = sigmoid(labels).reshape(-1)
    selected = np.argsort(-flattened, kind="stable")[:100]
    result = []
    for flattened_index in selected:
        index, label = divmod(int(flattened_index), labels.shape[1])
        if label != 1:
            continue
        raw = keypoints[index, 17:34]
        findability = sigmoid(raw[:, 2])
        log_l11, l21, log_l22 = raw[:, 4], raw[:, 5], raw[:, 6]
        log_trace = np.logaddexp.reduce(np.stack([
            -2 * log_l11, -2 * log_l22,
            2 * np.log(np.maximum(np.abs(l21), 1e-12)) - 2 * log_l11 - 2 * log_l22,
        ]), axis=0)
        log_mean_trace = np.logaddexp.reduce(log_trace + np.log(np.maximum(findability, 1e-12))) - np.logaddexp.reduce(np.log(np.maximum(findability, 1e-12)))
        score = float(sigmoid(np.log(flattened[flattened_index]) - TRACE_ALPHA * log_mean_trace))
        if score <= THRESHOLD:
            continue
        cx, cy, width, height = dets[index]
        left, top = max(0.0, float((cx - width / 2) * 640)), max(0.0, float((cy - height / 2) * 360))
        right, bottom = min(640.0, float((cx + width / 2) * 640)), min(360.0, float((cy + height / 2) * 360))
        if right - left < 2 or bottom - top < 2:
            continue
        points = np.empty((17, 3), dtype=np.float32)
        points[:, 0], points[:, 1], points[:, 2] = raw[:, 0] * 640, raw[:, 1] * 360, sigmoid(raw[:, 2])
        hips = points[[11, 12]]
        usable = hips[hips[:, 2] > 0.2]
        hip_x = float(np.average(usable[:, 0], weights=usable[:, 2])) if len(usable) else float(cx * 640)
        hip_y = float(np.average(usable[:, 1], weights=usable[:, 2])) if len(usable) else float(cy * 360)
        result.append({"Box": {"X": left, "Y": top, "Width": right - left, "Height": bottom - top}, "Score": score, "ClassId": 0, "HipX": hip_x, "HipY": hip_y, "HasHip": bool(len(usable)), "Seated": flexed_knee(points, 11, 13, 15) or flexed_knee(points, 12, 14, 16)})
    return result


parser = argparse.ArgumentParser()
parser.add_argument("--frames-glob", default=".playwright-mcp/compare*.bgr")
parser.add_argument("--output", default="docs/validation/pose-model-rfdetr.json")
parser.add_argument("--expected-frames", default=10, type=int)
args = parser.parse_args()
FRAMES = sorted((ROOT).glob(args.frames_glob), key=lambda path: path.name)
OUTPUT = ROOT / args.output

if not MODEL.exists() or len(FRAMES) != args.expected_frames:
    raise RuntimeError(f"Need exported model and {args.expected_frames} frames; model={MODEL.exists()}, frames={len(FRAMES)}")
providers = ort.get_available_providers()
if "CUDAExecutionProvider" not in providers:
    raise RuntimeError(f"CUDAExecutionProvider unavailable: {providers}")
session = ort.InferenceSession(str(MODEL), providers=["CUDAExecutionProvider"])
if "CUDAExecutionProvider" not in session.get_providers():
    raise RuntimeError(f"Session did not select CUDA: {session.get_providers()}")
input_name = session.get_inputs()[0].name
session.run(None, {input_name: preprocess(FRAMES[0])})
rows = []
for frame in FRAMES:
    frame_started = time.perf_counter()
    started = time.perf_counter()
    input_tensor = preprocess(frame)
    preprocessing_ms = (time.perf_counter() - started) * 1000
    started = time.perf_counter()
    dets, labels, keypoints = session.run(None, {input_name: input_tensor})
    inference_ms = (time.perf_counter() - started) * 1000
    started = time.perf_counter()
    people = decode(dets[0], labels[0], keypoints[0])
    decoding_ms = (time.perf_counter() - started) * 1000
    rows.append({
        "frame": frame.name,
        "poseMs": inference_ms,
        "preprocessingMs": preprocessing_ms,
        "decodingMs": decoding_ms,
        "modelDecodeMs": inference_ms + decoding_ms,
        "totalMs": (time.perf_counter() - frame_started) * 1000,
        "people": people,
    })
result = {"mode": "rfdetr-keypoint-preview-gpu-onnx", "model": "RFDETRKeypointPreview", "runtime": "ONNX Runtime CUDAExecutionProvider", "input": "640x360 BGR -> RGB -> 576x576 bilinear -> ImageNet normalization", "threshold": THRESHOLD, "result": rows}
OUTPUT.write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps({"providers": session.get_providers(), "frames": len(rows), "peoplePerFrame": [len(row["people"]) for row in rows]}, indent=2))
