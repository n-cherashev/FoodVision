"""Small CPU benchmark prototype. It performs no detection post-processing."""

from __future__ import annotations

import argparse
import statistics
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort


@dataclass(frozen=True)
class WorkerResult:
    worker_id: int
    frames_per_run: int
    timings: list[float]


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Benchmark ONNX inference over every video frame.")
    parser.add_argument("video", type=Path, help="Path to a readable video file")
    parser.add_argument("model", type=Path, help="Path to an ONNX model")
    parser.add_argument("runs", type=int, help="Number of complete video passes per worker")
    parser.add_argument("workers", type=int, nargs="?", default=1, help="Number of concurrent video workers")
    return parser.parse_args()


def preprocess(frame: np.ndarray, numpy_type: np.dtype) -> np.ndarray:
    # BGR -> RGB, resize to model input, scale to 0..1, HWC -> NCHW.
    image = cv2.resize(frame, (640, 640), interpolation=cv2.INTER_LINEAR)
    image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    image = image.astype(np.float32) / 255.0
    return np.ascontiguousarray(np.transpose(image, (2, 0, 1))[np.newaxis]).astype(numpy_type)


def create_session(model_path: Path) -> tuple[ort.InferenceSession, str, np.dtype]:
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model_path), options, providers=["CPUExecutionProvider"])
    model_input = session.get_inputs()[0]
    numpy_type = np.float16 if model_input.type == "tensor(float16)" else np.float32
    return session, model_input.name, numpy_type


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


def benchmark_worker(
    worker_id: int,
    video_path: Path,
    runs: int,
    session: ort.InferenceSession,
    input_name: str,
    numpy_type: np.dtype,
    start_barrier: threading.Barrier,
) -> WorkerResult:
    start_barrier.wait()
    timings: list[float] = []
    frames_per_run: int | None = None

    for run in range(1, runs + 1):
        frames, elapsed = benchmark_pass(video_path, session, input_name, numpy_type)
        if frames_per_run is not None and frames != frames_per_run:
            raise RuntimeError(f"Worker {worker_id} read {frames} frames in run {run}; expected {frames_per_run}")
        frames_per_run = frames
        timings.append(elapsed)

    assert frames_per_run is not None
    return WorkerResult(worker_id, frames_per_run, timings)


def main() -> None:
    args = parse_arguments()
    if args.runs < 1 or args.workers < 1:
        raise ValueError("runs and workers must be at least 1")
    if not args.video.is_file():
        raise FileNotFoundError(args.video)
    if not args.model.is_file():
        raise FileNotFoundError(args.model)

    # Each worker owns a one-thread ONNX session. This prevents nested parallelism.
    cv2.setNumThreads(1)
    sessions = [create_session(args.model) for _ in range(args.workers)]
    input_name = sessions[0][1]
    print(f"video={args.video} model={args.model} workers={args.workers} runs_per_worker={args.runs}")
    print(f"input={input_name}; cv_threads=1; ort_intra=1; ort_inter=1")

    start_barrier = threading.Barrier(args.workers + 1)
    with ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = [
            executor.submit(
                benchmark_worker,
                worker_id,
                args.video,
                args.runs,
                *session,
                start_barrier,
            )
            for worker_id, session in enumerate(sessions, start=1)
        ]
        started = time.perf_counter()
        start_barrier.wait()
        results = [future.result() for future in futures]
    elapsed = time.perf_counter() - started

    for result in results:
        mean = statistics.fmean(result.timings)
        print(
            f"worker={result.worker_id} runs={args.runs} frames_per_run={result.frames_per_run} "
            f"mean_seconds={mean:.3f} fps={result.frames_per_run / mean:.2f}"
        )

    all_timings = [timing for result in results for timing in result.timings]
    total_frames = sum(result.frames_per_run * args.runs for result in results)
    print(
        f"summary workers={args.workers} runs_per_worker={args.runs} total_frames={total_frames} "
        f"wall_seconds={elapsed:.3f} aggregate_fps={total_frames / elapsed:.2f} "
        f"mean_pass_seconds={statistics.fmean(all_timings):.3f} "
        f"min_pass_seconds={min(all_timings):.3f} max_pass_seconds={max(all_timings):.3f}"
    )


if __name__ == "__main__":
    main()
