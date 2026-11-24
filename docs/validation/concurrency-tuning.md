# GPU concurrency and telemetry checks

Date: 24 November 2026. Hardware: NVIDIA GeForce RTX 5070 Ti, driver 617.42, 16,303 MiB VRAM. Measurements used the current backend build in an isolated Docker container; the regular Compose services on ports 8080/8081 were left running and untouched. All benchmark runs used the same video, three ONNX models, 640×360 frames, 1 analytics FPS per stream, and independent FFmpeg decoders. GPU telemetry came from host `nvidia-smi` at one-second intervals. It reports the whole GPU, not per-process utilization; the baseline before the benchmark was 0% GPU and about 1,580 MiB VRAM in use.

## Concurrency sweep at 30 streams

Each run warmed up for 10 seconds, then measured a 20-second window. `Late` is the number of results that missed the one-second analytics deadline; `Dropped` counts analytics frames replaced in the bounded per-stream queue.

| GPU slots | Analytics FPS total | FPS/stream | Late | Dropped | GPU avg / peak | VRAM avg / peak (MiB) | Queue p95 avg (ms) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 26.10 | 0.870 | 520 | 79 | 84% / 91% | 7,476 / 7,477 | 1,085 |
| 6 | 27.11 | 0.904 | 529 | 58 | 89% / 96% | 9,646 / 9,647 | 961 |
| 8 | 27.65 | 0.922 | 530 | 53 | 90% / 95% | 11,808 / 11,810 | 907 |
| 10 | **28.40** | 0.947 | 550 | 38 | 92% / 96% | 13,667 / 13,684 | 782 |
| 12 | 24.95 | 0.832 | 476 | 74 | 92% / 98% | **15,915 / 15,918** | 950 |

Ten slots gave the highest measured throughput. Raising the limit to 12 reduced throughput by about 12% and left only about 385 MiB below total VRAM capacity. Do not raise the production limit above 10 on this machine based on these results.

## Camera-count sweep at 10 GPU slots

| Streams | Total analytics FPS | GPU avg / peak | VRAM avg (MiB) | Late | Dropped |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 9.95 | 78% / 84% | 11,947 | 0 | 0 |
| 20 | 19.96 | 60% / 71% | 11,947 | 0 | 0 |
| 27 | 27.35 | 69% / 95% | 11,951 | 0 | 0 |

These are short 20-second measurements, so GPU utilization varies with the synchronized arrival of sampled frames. The 27-stream figure was checked again over the full source video below.

## Full run at 27 streams

The full run lasted 210.732 seconds. All 27 streams completed 211 analytical inferences each: 5,697 total, or **27.03 FPS system-wide / 1.001 FPS per stream**. There were no dropped frames or stream errors. Twenty-five results missed the one-second budget, all during the first 30 seconds; none were added afterwards.

Across the full run, GPU utilization averaged **88.9%** and peaked at 98%; VRAM averaged 11,982 MiB and peaked at 12,004 MiB. GPU power averaged 228 W and the SM clock averaged 2,779 MHz. Backend CPU averaged 34.6% by the app's process metric; working set averaged about 3,123 MiB. The GPU utilization is host-wide and may include other GPU work; the idle baseline was near zero.

Average per-frame stages were approximately 2.7 ms preprocessing, 80.7 ms table detector, 145.8 ms pose model, and 96.7 ms person detector. The pose model was the largest measured stage. The full-run GPU queue wait averaged 270 ms; per-stream queue-wait p95 averaged 326 ms and result-latency p95 averaged 576 ms.

The short camera-count runs delivered video to the default first 10 subscriptions; their WebSocket client discarded binary frames. The 27-stream full run also used a raw WebSocket client, not 27 browser canvases. These measurements establish backend analytics capacity, not the maximum browser rendering or full-video-delivery capacity.

## Interpretation

The GPU is not continuously at 100% at every stream count, but it is close to saturation at the measured operating limit: the full 27-stream run averaged 89% and peaked at 98%, while a 30-stream overload averaged 92%. Meanwhile, the 10-slot queue accumulated about 270 ms of average wait at 27 streams. At 30 streams, throughput stopped keeping pace with offered work and frames were late or dropped.

The slot sweep confirms that adding concurrency is not a free throughput increase. Ten is the best tested setting; 12 nearly filled VRAM and performed worse. The current useful optimization direction is to reduce time per inference, starting with the pose model, or to evaluate model/runtime alternatives under the same accuracy and latency checks. Collect another GPU profile before making kernel-level or I/O-copy changes; the current telemetry already shows substantial device utilization.

## Raw measurements

- [27-stream full-run metrics](benchmark-capacity-27-full.json)
- [10/20/27-stream GPU sweep](stream-count-gpu-sweep.json)
- [Concurrency 4](concurrency-4.json), [6](concurrency-6.json), [8](concurrency-8-repeat.json), [10](concurrency-10.json), [12](concurrency-12.json)
- [One-second GPU telemetry samples](gpu-telemetry-sweep.csv)
