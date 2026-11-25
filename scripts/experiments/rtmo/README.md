# RTMO probe

This isolated probe runs the official OpenMMLab RTMO ONNX SDK artifact through
`rtmlib` and ONNX Runtime GPU. It does not modify the FoodVision application or
copy a model into `models/`.

Run from the repository root:

```powershell
scripts/experiments/rtmo/run-probe.ps1 -Variant m -Second 10 -Runs 10
```

The script downloads the official RTMO-M package from OpenMMLab, verifies its
captured SHA-256, extracts it under `.playwright-mcp/rtmo/`, builds the isolated
CUDA 13 container, and emits JSON with the CUDA provider, detected people,
keypoint count, and post-warm-up latency.

To compare the same raw frames as the existing person-model baseline:

```powershell
scripts/experiments/rtmo/run-probe.ps1 -Variant m -Runs 10 -CompareFrames
```

This writes `.playwright-mcp/rtmo/rtmo-m-comparison.json` as an array matching
the baseline's `{ model, frame, ms, count, boxes }` records. Each box also has
`hipX`, `hipY`, `hasHip`, and `seated`. Boxes come directly from RTMO's `dets`
ONNX output; the script never derives them from keypoints. It uses the SDK's
`BottomupResize` contract: BGR input, aspect-ratio resize, top-left placement
on a 114-valued 640×640 canvas, and official score/NMS defaults of 0.1/0.65.

Sources: [official RTMO model index](https://github.com/open-mmlab/mmpose/blob/main/configs/body_2d_keypoint/rtmo/body7/rtmo_body7.yml), [official ONNX SDK URL used by rtmlib](https://github.com/Tau-J/rtmlib/blob/main/rtmlib/tools/solution/body.py), and [MMPose Apache-2.0 license](https://github.com/open-mmlab/mmpose/blob/main/LICENSE). The licence and provenance of each downloaded checkpoint must be verified before production use.
