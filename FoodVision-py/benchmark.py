"""Small CPU benchmark prototype. It performs no detection post-processing."""

from __future__ import annotations

import argparse
import statistics
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Benchmark ONNX inference over every video frame.")
    parser.add_argument("video", type=Path, help="Path to a readable video file")
    parser.add_argument("model", type=Path, help="Path to an ONNX model")
    parser.add_argument("runs", type=int, help="Number of complete video passes")
    return parser.parse_args()


def preprocess(frame: np.ndarray, numpy_type: np.dtype) -> np.ndarray:
    # BGR -> RGB, resize to model input, scale to 0..1, HWC -> NCHW.
    image = cv2.resize(frame, (640, 640), interpolation=cv2.INTER_LINEAR)
    image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    image = image.astype(np.float32) / 255.0
    return np.ascontiguousarray(np.transpose(image, (2, 0, 1))[np.newaxis]).astype(numpy_type)


def benchmark_pass(
    video_path: Path,
    session: ort.InferenceSession,
    input_name: str,
    numpy_type: np.dtype,
) -> tuple[int, float]:
    capture = cv2.VideoCapture(str(video_path))
    if not capture.isOpened():
        raise RuntimeError(f"Cannot open video: {video_path}")

    frame_count = 0
    started = time.perf_counter()
    while True:
        read, frame = capture.read()
        if not read:
            break
        session.run(None, {input_name: preprocess(frame, numpy_type)})
        frame_count += 1
    elapsed = time.perf_counter() - started
    capture.release()
    return frame_count, elapsed


def main() -> None:
    args = parse_arguments()
    if args.runs < 1:
        raise ValueError("runs must be at least 1")
    if not args.video.is_file():
        raise FileNotFoundError(args.video)
    if not args.model.is_file():
        raise FileNotFoundError(args.model)

    # Fixed thread settings make the C# and Python tests more comparable.
    cv2.setNumThreads(1)
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(args.model), options, providers=["CPUExecutionProvider"])
    model_input = session.get_inputs()[0]
    numpy_type = np.float16 if model_input.type == "tensor(float16)" else np.float32

    print(f"video={args.video} model={args.model} runs={args.runs}")
    print(f"input={model_input.name} {model_input.shape} {model_input.type}; cv_threads=1; ort_intra=1; ort_inter=1")
    timings: list[float] = []
    frame_count: int | None = None
    for index in range(1, args.runs + 1):
        frames, elapsed = benchmark_pass(args.video, session, model_input.name, numpy_type)
        if frame_count is not None and frames != frame_count:
            raise RuntimeError(f"Video yielded {frames} frames in run {index}; expected {frame_count}")
        frame_count = frames
        timings.append(elapsed)
        print(f"run={index} frames={frames} seconds={elapsed:.3f} fps={frames / elapsed:.2f}")

    assert frame_count is not None
    mean = statistics.fmean(timings)
    print(
        f"summary runs={args.runs} frames_per_run={frame_count} "
        f"mean_seconds={mean:.3f} mean_fps={frame_count / mean:.2f} "
        f"min_seconds={min(timings):.3f} max_seconds={max(timings):.3f}"
    )


if __name__ == "__main__":
    main()
