# Capacity benchmark

## Result

On the tested NVIDIA GeForce RTX 5070 Ti, the current pipeline sustained **about 27 analytical frames/s total** while keeping **27 independent streams near the 1 analytical frame/s target per stream** for the full 210.7-second source video. All 27 streams completed, each processed 211 frames, no analytics frames were dropped, and 14 results exceeded the one-second deadline during startup; none were added after the first 30 seconds.

Treat 27 streams as the measured ceiling for this machine and configuration, not as a general deployment guarantee. For operating headroom, use 25 streams or fewer. A 28-stream short run still averaged more than 1 FPS per stream, but 118 results exceeded the one-second deadline during its 20-second measurement window. At 29 streams the system fell below target (0.969 FPS/stream), with 440 late results and 9 dropped frames. At 30–50 streams throughput plateaued near 29 FPS total while per-stream rate and latency degraded.

Follow-up GPU telemetry and concurrency-limit comparisons are recorded in [concurrency-tuning.md](concurrency-tuning.md).

## Short-run sweep

Each short run measured a 20-second window after a 10-second warm-up. The endpoint reported cumulative counts per stream; this table uses count deltas from the measurement window. `Late` counts analytical results completed after their one-second deadline. GPU queue and result latency p95 values are the averages of per-stream p95 values at the end of the window.

| Streams | Total analytics FPS | FPS per stream | Late | Dropped | GPU wait p95 avg (ms) | Result latency p95 avg (ms) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 10.04 | 1.004 | 0 | 0 | <1 | 280 |
| 20 | 20.50 | 1.025 | 0 | 0 | 191 | 434 |
| 21 | 21.04 | 1.002 | 0 | 0 | 276 | 508 |
| 25 | 24.96 | 0.998 | 0 | 0 | 354 | 627 |
| 27 | 26.84 | 0.994 | 0 | 0 | 429 | 759 |
| 28 | 28.52 | 1.018 | 118 | 0 | 549 | 897 |
| 29 | 28.09 | 0.969 | 440 | 9 | 748 | 1,592 |
| 30 | 29.05 | 0.968 | 528 | 25 | 751 | 1,820 |
| 40 | 28.95 | 0.724 | 579 | 220 | 1,092 | 2,247 |
| 50 | 29.25 | 0.585 | 585 | 422 | 1,473 | 2,667 |

The highest short-run aggregate throughput observed was 29.25 FPS at 50 streams, but that is an overloaded point, not a usable 1 FPS-per-stream capacity. The highest full-video run was 27.04 FPS total at 27 streams. GPU inference concurrency was 10; decoding remained independent per stream and inference requests waited for one of the 10 GPU slots.

## Full-video run at 27 streams

- Source: `videos/Sample_Video_1.mp4`, 1280×720, 30 FPS, 210.167 seconds.
- Models: `yolo26l-detect.onnx`, `yolo11x-pose.onnx`, `yolo11x.onnx`; target analytics rate 1 FPS/stream; GPU inference concurrency 10.
- Hardware: NVIDIA GeForce RTX 5070 Ti, 16,303 MiB VRAM, driver 617.42. Backend process CPU averaged 64%; working set was about 3,294 MiB. GPU utilization and application VRAM use were unavailable because NVIDIA telemetry is not installed in the image.
- Result: 27/27 streams completed in 210.702 seconds; 28,377 frames accepted and 5,697 independently processed (211 per stream); 14 late analytics results, 0 dropped frames, 0 stream errors. Aggregate processing rate was 27.04 FPS, or 1.001 FPS/stream.
- Average per-stream inference time was 324 ms; average per-stream result latency p95 was 556 ms. These are backend measurements; browser rendering latency was not measured in this capacity run.

The short-run capacity sweep's single WebSocket client discarded video frames. The server's default view range still delivered video for streams 1–10; video delivery and browser rendering for all 27 streams were not measured. This capacity result describes the analytics pipeline on this host. A separate browser run is needed to establish the maximum number of streams that can also be rendered at once.

## Reproduction

The benchmark defaults to 10 streams, matching the regular UI. For capacity diagnostics, the WebSocket endpoint accepts `?streams=N` with `N` from 1 to 50; the UI remains fixed at 10. Build and run the backend with the models and source video mounted as described in the main README, then use Node 22 or newer:

```powershell
node scripts/benchmark-capacity.mjs ws://localhost:8080/api/benchmark 10,20,30,40,50
```

The script warms up for 10 seconds, measures a 20-second interval, then stops each run. It writes the raw per-stream snapshots to `docs/validation/benchmark-capacity-latest.json`. Run a second sweep with finer counts around the first overloaded point to locate the real-time boundary. Full-run per-stream measurements for this result are in [benchmark-capacity-27-full.json](benchmark-capacity-27-full.json); short-run snapshots are in [sweep](benchmark-capacity-sweep.json), [boundary](benchmark-capacity-boundary.json), and [final](benchmark-capacity-final.json).
