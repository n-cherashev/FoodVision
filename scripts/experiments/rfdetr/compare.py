"""Filter RF-DETR output at a declared threshold and compare it with FoodVision baseline."""

import argparse
import json
import statistics
from pathlib import Path

ROOT = Path("/work")
parser = argparse.ArgumentParser()
parser.add_argument("--baseline", default="docs/validation/pose-model-baseline.json")
parser.add_argument("--candidate", default="docs/validation/pose-model-rfdetr.json")
parser.add_argument("--output", default="docs/validation/pose-model-rfdetr-comparison.json")
parser.add_argument("--threshold", default=0.4, type=float)
args = parser.parse_args()
baseline = json.loads((ROOT / args.baseline).read_text(encoding="utf-8"))
candidate = json.loads((ROOT / args.candidate).read_text(encoding="utf-8"))
threshold = args.threshold


def iou(a, b):
    a, b = a["Box"], b["Box"]
    overlap = max(0, min(a["X"] + a["Width"], b["X"] + b["Width"]) - max(a["X"], b["X"])) * max(0, min(a["Y"] + a["Height"], b["Y"] + b["Height"]) - max(a["Y"], b["Y"]))
    return overlap / (a["Width"] * a["Height"] + b["Width"] * b["Height"] - overlap)


candidate_by_frame = {row["frame"]: row for row in candidate["result"]}
if len(candidate_by_frame) != len(candidate["result"]):
    raise ValueError("Candidate has duplicate frame names")
if {row["frame"] for row in baseline["result"]} != set(candidate_by_frame):
    raise ValueError("Baseline and candidate frame sets differ")

rows = []
for old in baseline["result"]:
    new = candidate_by_frame[old["frame"]]
    people = [item for item in new["people"] if item["Score"] >= threshold]
    pairs = sorted(((iou(a, b), i, j) for i, a in enumerate(old["people"]) for j, b in enumerate(people)), reverse=True)
    used_old, used_new, matched = set(), set(), []
    for overlap, i, j in pairs:
        if overlap < 0.5:
            break
        if i not in used_old and j not in used_new:
            used_old.add(i); used_new.add(j); matched.append((old["people"][i], people[j]))
    rows.append({"frame": old["frame"], "baseline": len(old["people"]), "candidate": len(people), "matchedIoU50": len(matched), "seatedDisagreement": sum(a["Seated"] != b["Seated"] for a, b in matched), "bothHaveHip": sum(a["HasHip"] and b["HasHip"] for a, b in matched)})

output = {
    "candidate": candidate["model"], "threshold": threshold, "frames": rows,
    "totals": {key: sum(row[key] for row in rows) for key in ("baseline", "candidate", "matchedIoU50", "seatedDisagreement", "bothHaveHip")},
    "baselineMedianPoseMs": statistics.median(row["poseMs"] for row in baseline["result"]),
    "baselineMedianTwoModelMs": statistics.median(row["totalMs"] for row in baseline["result"]),
    "candidateMedianOnnxInferenceMs": statistics.median(row["poseMs"] for row in candidate["result"]),
    "candidateMedianPreprocessingMs": statistics.median(row["preprocessingMs"] for row in candidate["result"]),
    "candidateMedianDecodingMs": statistics.median(row["decodingMs"] for row in candidate["result"]),
    "candidateMedianModelDecodeMs": statistics.median(row["modelDecodeMs"] for row in candidate["result"]),
    "candidateMedianTotalMs": statistics.median(row["totalMs"] for row in candidate["result"]),
}
(ROOT / args.output).write_text(json.dumps(output, indent=2), encoding="utf-8")
print(json.dumps(output["totals"], indent=2))
