"""Compare RTMO fixed-frame outputs with FoodVision's existing person pipeline.

This measures agreement with the current implementation, not ground-truth
accuracy. Both files are expected to contain the same fixed source frames.
"""

import json
import statistics
import sys
from pathlib import Path


def xyxy(box: dict, baseline: bool) -> tuple[float, float, float, float]:
    if baseline:
        box = box["Box"]
        return box["X"], box["Y"], box["X"] + box["Width"], box["Y"] + box["Height"]
    return box["x"], box["y"], box["x"] + box["width"], box["y"] + box["height"]


def iou(first: tuple[float, float, float, float], second: tuple[float, float, float, float]) -> float:
    left, top = max(first[0], second[0]), max(first[1], second[1])
    right, bottom = min(first[2], second[2]), min(first[3], second[3])
    intersection = max(0, right - left) * max(0, bottom - top)
    union = (first[2] - first[0]) * (first[3] - first[1]) + (second[2] - second[0]) * (second[3] - second[1]) - intersection
    return intersection / union if union else 0


baseline_path, rtmo_path, output_path = map(Path, sys.argv[1:4])
baseline = json.loads(baseline_path.read_text(encoding="utf-8"))["result"]
rtmo = json.loads(rtmo_path.read_text(encoding="utf-8"))
by_frame = {item["frame"]: item for item in rtmo}
rows = []

for previous in baseline:
    candidate = by_frame.pop(previous["frame"])
    pairs = sorted(
        (
            (iou(xyxy(old, True), xyxy(new, False)), old_index, new_index)
            for old_index, old in enumerate(previous["people"])
            for new_index, new in enumerate(candidate["boxes"])
        ),
        reverse=True,
    )
    used_old, used_new, matches = set(), set(), []
    for overlap, old_index, new_index in pairs:
        if overlap < 0.5:
            break
        if old_index not in used_old and new_index not in used_new:
            used_old.add(old_index)
            used_new.add(new_index)
            matches.append((previous["people"][old_index], candidate["boxes"][new_index]))
    rows.append({
        "frame": previous["frame"],
        "baseline": len(previous["people"]),
        "rtmo": len(candidate["boxes"]),
        "matchedIouAtLeast0_5": len(matches),
        "seatedDisagreement": sum(old["Seated"] != new["seated"] for old, new in matches),
        "bothHaveHip": sum(old["HasHip"] and new["hasHip"] for old, new in matches),
    })

if by_frame:
    raise ValueError(f"RTMO contains unexpected frames: {sorted(by_frame)}")

summary = {
    "metric": "agreement with current FoodVision output; not ground-truth accuracy",
    "iouThreshold": 0.5,
    "frames": rows,
    "totals": {key: sum(row[key] for row in rows) for key in ("baseline", "rtmo", "matchedIouAtLeast0_5", "seatedDisagreement", "bothHaveHip")},
    "baselineMedianTotalMs": statistics.median(item["totalMs"] for item in baseline),
    "rtmoMedianModelInvocationMs": statistics.median(item["ms"] for item in rtmo),
}
output_path.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(summary, ensure_ascii=False, indent=2))
