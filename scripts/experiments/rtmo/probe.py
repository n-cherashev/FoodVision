"""Run official RTMO ONNX package on a fixed FoodVision video frame."""

import argparse
import glob
import json
import math
import re
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort


parser = argparse.ArgumentParser()
parser.add_argument("--model", type=Path, required=True)
source = parser.add_mutually_exclusive_group(required=True)
source.add_argument("--video", type=Path)
source.add_argument("--bgr-glob", help="Glob for 640×360 BGR24 frames, e.g. /work/.playwright-mcp/compare*.bgr")
parser.add_argument("--output", type=Path, help="Write baseline-compatible detections JSON for --bgr-glob")
parser.add_argument("--model-name", help="Name written to baseline-compatible JSON")
parser.add_argument("--second", type=float, default=10.0)
parser.add_argument("--runs", type=int, default=10)
parser.add_argument("--score-threshold", type=float, default=.1)
parser.add_argument("--nms-threshold", type=float, default=.65)
args = parser.parse_args()


def read_video_frame(video: Path, second: float) -> tuple[str, "np.ndarray"]:
    capture = cv2.VideoCapture(str(video))
    capture.set(cv2.CAP_PROP_POS_MSEC, second * 1000)
    ok, frame = capture.read()
    capture.release()
    if not ok:
        raise RuntimeError(f"Could not read {second}s from {video}")
    return video.name, frame


def read_bgr_frame(path: Path) -> tuple[str, "np.ndarray"]:
    expected = 640 * 360 * 3
    data = np.fromfile(path, dtype=np.uint8)
    if data.size != expected:
        raise ValueError(f"{path} contains {data.size} bytes; expected {expected} for BGR24 640×360")
    return path.name, data.reshape(360, 640, 3)


def preprocess(frame: "np.ndarray") -> tuple["np.ndarray", float]:
    """Replicate the official RTMO ONNX SDK BottomupResize behavior."""
    source_height, source_width = frame.shape[:2]
    ratio = min(640 / source_height, 640 / source_width)
    resized_height = int(source_height * ratio)
    resized_width = int(source_width * ratio)
    padded = np.full((640, 640, 3), 114, dtype=np.uint8)
    resized = cv2.resize(frame, (resized_width, resized_height), interpolation=cv2.INTER_LINEAR)
    padded[:resized_height, :resized_width] = resized
    return padded.transpose(2, 0, 1)[None].astype(np.float32), ratio


def iou_xyxy(box: "np.ndarray", boxes: "np.ndarray") -> "np.ndarray":
    left = np.maximum(box[0], boxes[:, 0])
    top = np.maximum(box[1], boxes[:, 1])
    right = np.minimum(box[2], boxes[:, 2])
    bottom = np.minimum(box[3], boxes[:, 3])
    intersection = np.maximum(0, right - left + 1) * np.maximum(0, bottom - top + 1)
    area = (box[2] - box[0] + 1) * (box[3] - box[1] + 1)
    other_area = (boxes[:, 2] - boxes[:, 0] + 1) * (boxes[:, 3] - boxes[:, 1] + 1)
    return intersection / (area + other_area - intersection)


def nms_xyxy(boxes: "np.ndarray", scores: "np.ndarray", threshold: float) -> "np.ndarray":
    order = scores.argsort()[::-1]
    keep = []
    while order.size:
        selected = order[0]
        keep.append(selected)
        order = order[1:][iou_xyxy(boxes[selected], boxes[order[1:]]) <= threshold]
    return np.asarray(keep, dtype=np.int64)


def knee_is_flexed(points: "np.ndarray", hip: int, knee: int, ankle: int) -> bool:
    a, b, c = points[hip], points[knee], points[ankle]
    if a[2] < .2 or b[2] < .2 or c[2] < .2:
        return False
    ab_x, ab_y = a[0] - b[0], a[1] - b[1]
    cb_x, cb_y = c[0] - b[0], c[1] - b[1]
    denominator = math.sqrt((ab_x * ab_x + ab_y * ab_y) * (cb_x * cb_x + cb_y * cb_y))
    if denominator < 1:
        return False
    angle = math.degrees(math.acos(max(-1, min(1, (ab_x * cb_x + ab_y * cb_y) / denominator))))
    return 55 <= angle <= 145


def decode_raw(detections: "np.ndarray", keypoints: "np.ndarray", ratio: float, source_width: int, source_height: int) -> list[dict]:
    """Decode official `dets`/`keypoints`; boxes always originate from `dets`."""
    boxes = detections[0, :, :4] / ratio
    scores = detections[0, :, 4]
    points = keypoints[0].copy()
    points[:, :, :2] /= ratio
    candidates = np.flatnonzero(scores > args.score_threshold)
    if not len(candidates):
        return []
    kept = candidates[nms_xyxy(boxes[candidates], scores[candidates], args.nms_threshold)]
    results = []
    for index in kept:
        left, top, right, bottom = boxes[index]
        left, top = max(0., left), max(0., top)
        right, bottom = min(float(source_width), right), min(float(source_height), bottom)
        if right - left < 2 or bottom - top < 2:
            continue
        person_points = points[index]
        hip_points = person_points[[11, 12]]
        hip_points = hip_points[hip_points[:, 2] > .2]
        has_hip = len(hip_points) > 0
        center_x, center_y = (left + right) / 2, (top + bottom) / 2
        hip_x = float(np.average(hip_points[:, 0], weights=hip_points[:, 2])) if has_hip else center_x
        hip_y = float(np.average(hip_points[:, 1], weights=hip_points[:, 2])) if has_hip else center_y
        results.append({
            "score": float(scores[index]),
            "x": float(left),
            "y": float(top),
            "width": float(right - left),
            "height": float(bottom - top),
            "hipX": float(hip_x),
            "hipY": float(hip_y),
            "hasHip": bool(has_hip),
            "seated": bool(knee_is_flexed(person_points, 11, 13, 15) or knee_is_flexed(person_points, 12, 14, 16)),
        })
    return results


providers = ort.get_available_providers()
if "CUDAExecutionProvider" not in providers:
    raise RuntimeError(f"CUDAExecutionProvider unavailable: {providers}")
session = ort.InferenceSession(str(args.model), providers=["CUDAExecutionProvider"])
input_name = session.get_inputs()[0].name
output_names = ["dets", "keypoints"]
available_outputs = {item.name for item in session.get_outputs()}
if set(output_names) - available_outputs:
    raise RuntimeError(f"Expected RTMO SDK outputs {output_names}, received {sorted(available_outputs)}")

if args.bgr_glob:
    def frame_order(path: str) -> tuple[int, str]:
        match = re.search(r"(\d+)", Path(path).stem)
        return (int(match.group(1)), path) if match else (0, path)

    paths = [Path(path) for path in sorted(glob.glob(args.bgr_glob), key=frame_order)]
    if not paths:
        raise FileNotFoundError(f"No BGR frames matched {args.bgr_glob}")
    frames = [read_bgr_frame(path) for path in paths]
else:
    frames = [read_video_frame(args.video, args.second)]

reports = []
for frame_name, frame in frames:
    input_tensor, ratio = preprocess(frame)
    session.run(output_names, {input_name: input_tensor})  # Warm up outside the measurement.
    durations_ms = []
    outputs = None
    for _ in range(args.runs):
        started = time.perf_counter()
        outputs = session.run(output_names, {input_name: input_tensor})
        durations_ms.append((time.perf_counter() - started) * 1000)
    boxes = decode_raw(outputs[0], outputs[1], ratio, frame.shape[1], frame.shape[0])
    reports.append({
        "model": args.model_name or args.model.parent.name,
        "frame": frame_name,
        "ms": sum(durations_ms) / len(durations_ms),
        "count": len(boxes),
        "boxes": boxes,
    })

encoded = json.dumps(reports, ensure_ascii=False, indent=2)
if args.output:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(encoded, encoding="utf-8")
print(encoded)
