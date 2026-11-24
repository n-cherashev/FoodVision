# Issue #4 baseline run

Date: 2026-11-24

## Environment

- GPU: NVIDIA GeForce RTX 5070 Ti, 16,303 MiB VRAM; Docker Server 29.9.3.
- Models: `yolo26l-detect.onnx`, `yolo11x-pose.onnx`, `yolo11x.onnx`.
- Video: `videos/Sample_Video_1.mp4` (1280x720, 30 FPS, ~210.17 s).
- Configuration: analytics 1 FPS, delivery 5 FPS at 640x360, GPU concurrency 2.
- Existing compose containers were left running and were not used.

## Result

`git archive e38a466402f1b5905faf579dd7237afbbf2c97a9` was exported to `E:\Temp\FoodVision-head-issue4b`; the
original Dockerfile built successfully as `foodvision-head-baseline`. A separate
container on port 18080 returned HTTP 200 from `/health`.

A Node 24 WebSocket client sent `{"action":"start"}`, ran 30 seconds across
50 streams, then sent `{"action":"stop"}`. Latest-per-stream aggregates:
delivered FPS 4.23; analytics FPS 0.43; accepted 6,281; processed 638; CPU
47.9%; working set 2,686 MiB; latency average 2,696 ms; latency p95 3,398 ms;
inference average 88.3 ms; queue wait average 2,028 ms. This is a bounded load
sample, not a full 210-second completion result.

The existing full baseline remains in
[`benchmark-10-streams.md`](benchmark-10-streams.md): 10 streams, 210.9 s,
1,051 delivered frames/stream, 211 analytical frames/stream, inference ~91 ms,
GPU wait ~0.62 s average and ~0.76 s p95.

## Reproduction and limitations

Export `git archive e38a466402f1b5905faf579dd7237afbbf2c97a9`, build `backend/Dockerfile`, run on port 18080 with
read-only mounts for `models/` and `videos/` and the model paths above. Connect
to `ws://localhost:18080/api/benchmark`, send `{"action":"start"}`, then
`{"action":"stop"}` after the desired interval. GPU/VRAM telemetry was
unavailable because NVIDIA telemetry is not installed; client render latency was
not measured (`clientLatencyMs` was null).

Remove the temporary archive, image, and container after reproduction.

