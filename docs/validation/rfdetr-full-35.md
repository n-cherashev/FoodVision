# RF-DETR Keypoint Preview + YOLO11x: full-video run at 35 streams and capacity sweep

25 November 2026. An isolated benchmark build in `.playwright-mcp/rf-benchmark-worktree` replaced the two original person models (`yolo11x-pose.onnx` + `yolo26l-detect.onnx`) with a single `rfdetr-keypoint-preview.onnx` call (confidence threshold `0.40`, `FOODVISION_BENCHMARK_PERSON_PIPELINE=rfdetr-keypoint`), keeping `yolo11x.onnx` unchanged for tables and belongings. Unlike the YOLO26s benchmark build, the old person-pose and person-detector sessions were not loaded into VRAM. GPU concurrency `10`; one `Sample_Video_1.mp4` per independent stream; target `1` analytics FPS per stream.

Before the GPU run, C# preprocessing (`RfDetrKeypointInput`) and decoding (`RfDetrKeypointModel.Decode`) were checked on CPU against the Python reference (`scripts/experiments/rfdetr/probe.py`) on `early-scene-025s.bgr` and `late-scene-a-145s.bgr`: 7/7 and 10/10 detections matched with `maxTensorDiff <= 1.16e-4`, `maxBoxDiff <= 6.10e-5` px, `maxScoreDiff <= 5.96e-8`, `maxHipDiff <= 6.10e-5` px, and `0` `HasHip`/`Seated` mismatches.

## Full-video run at 35 streams

All **35 streams processed 211 frames**, 7,385 in total, in **210.404 s** (35.10 analytics FPS system-wide). There were **2 late results** (`overBudget`: 1 frame each on streams 26 and 34, max observed latency `1,048.62 ms`), **0 dropped frames**, and **0 stream errors**.

- Mean across streams of per-stream inference: **201.04 ms** (mean p95 **244.49 ms**): preprocessing **8.83 ms**, `yolo11x.onnx` **99.02 ms**, `rfdetr-keypoint-preview.onnx` + decode **93.14 ms**.
- Mean across streams of per-stream GPU-queue wait: **198.91 ms** (mean p95 **242.83 ms**).
- Mean across streams of per-stream result latency: **326.68 ms** (mean p95 **394.09 ms**).
- Backend process CPU / RAM (final snapshot mean): **40.97%** CPU, **2,361 MiB** working set.
- Host-wide `nvidia-smi` telemetry across 209 one-second samples ([rfdetr-full-35-gpu.csv](rfdetr-full-35-gpu.csv)): GPU utilization mean **70.8%** (peak **95.0%**), VRAM mean **11,700 MiB** (peak **12,180 MiB** of 16,303 MiB).

Raw WebSocket snapshots: [rfdetr-full-35.json](rfdetr-full-35.json). Browser rendering and video delivery beyond the default 10-stream view were not measured.

## Short-run capacity sweep (10 s warm-up, 20 s measurement)

| Streams | Total analytics FPS | FPS / stream | Late | Dropped | Avg inference (ms) | Queue wait p95 avg (ms) | Latency p95 avg (ms) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 20 *(cold 1st run)* | 19.80 | 0.990 | 396 | 0 | 217.3 | 239.1 | 1,771.3 |
| 25 | 25.24 | 1.010 | 0 | 0 | 188.5 | 151.5 | 254.2 |
| 30 | 30.24 | 1.008 | 0 | 0 | 212.4 | 208.0 | 373.9 |
| 35 | 35.69 | 1.020 | 0 | 0 | 201.1 | 273.7 | 431.3 |
| 40 | 39.78 | 0.995 | 0 | 0 | 218.2 | 420.7 | 663.1 |
| 45 | 46.98 | 1.044 | 377 | 0 | 218.1 | 801.1 | 1,161.5 |
| 50 | 45.82 | 0.916 | 880 | 65 | 215.5 | 934.7 | 1,949.7 |

Sweep raw files: [pose-model-rfdetr-capacity.json](pose-model-rfdetr-capacity.json) (20–40) and [pose-model-rfdetr-capacity-high.json](pose-model-rfdetr-capacity-high.json) (45–50).

## Limitations

Neither this run nor the [YOLO26s-pose 35-stream run](yolo26s-full-35.md) measures recognition accuracy or seating-transition correctness against labelled ground truth. Frame-level differences between RF-DETR (at uncalibrated threshold 0.40) and the current two-model baseline are recorded in [pose-60-rfdetr.md](pose-60-rfdetr.md).
