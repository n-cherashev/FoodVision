# Experiment 2: reuse posture for one frame

24 November 2026. Experimental benchmark only: run the pose model on alternate accepted analytics frames per stream, reusing the previous pose boxes and keypoints on the next frame without motion compensation. Both detectors still run on every frame. The application algorithm was not changed. See the [experimental patch](optimization-experiment.patch).

Using the same video, 640×360, 1 analytics FPS, 10 GPU slots, 10-second warm-up and 20-second window as the [baseline](concurrency-tuning.md), 30 streams delivered **29.80 FPS**, 6 late, 0 dropped; the earlier 30-stream baseline delivered 28.40 FPS, 550 late and 38 dropped. At 40 streams the experiment delivered **39.69 FPS**, 402 late and 0 dropped, so the one-second deadline was frequently missed even though processed FPS nearly matched input. One short run per configuration: these results do not establish a stable capacity or browser rendering performance. [Raw metrics](optimization-pose-every-two.json).

Quality was not assessed. Stale pose coordinates may be matched to the wrong person after movement, or delay transitions such as sitting down and standing up. Frame drops also make the reuse interval longer than one second. The experiment is not suitable for deployment without a frame-aligned comparison, tracking/motion handling and labelled state transitions.
