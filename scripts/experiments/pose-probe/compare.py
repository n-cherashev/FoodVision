"""Compare candidate detections with baseline; agreement is not ground-truth accuracy."""
import json
import statistics
import sys

baseline, candidate = (json.load(open(path, encoding="utf-8-sig")) for path in sys.argv[1:3])

def iou(a, b):
    a, b = a["Box"], b["Box"]
    intersection = max(0, min(a["X"] + a["Width"], b["X"] + b["Width"]) - max(a["X"], b["X"])) * max(0, min(a["Y"] + a["Height"], b["Y"] + b["Height"]) - max(a["Y"], b["Y"]))
    return intersection / (a["Width"] * a["Height"] + b["Width"] * b["Height"] - intersection)

rows = []
for old, new in zip(baseline["result"], candidate["result"], strict=True):
    assert old["frame"] == new["frame"]
    pairs = sorted(((iou(a, b), i, j) for i, a in enumerate(old["people"]) for j, b in enumerate(new["people"])), reverse=True)
    used_old, used_new, matched = set(), set(), []
    for overlap, i, j in pairs:
        if overlap < .5:
            break
        if i not in used_old and j not in used_new:
            used_old.add(i); used_new.add(j); matched.append((old["people"][i], new["people"][j]))
    rows.append({"frame": old["frame"], "baseline": len(old["people"]), "candidate": len(new["people"]), "matched": len(matched), "seatedDisagreement": sum(a["Seated"] != b["Seated"] for a, b in matched), "bothHaveHip": sum(a["HasHip"] and b["HasHip"] for a, b in matched)})

print(json.dumps({"frames": rows, "totals": {key: sum(row[key] for row in rows) for key in ("baseline", "candidate", "matched", "seatedDisagreement", "bothHaveHip")}, "baselineMedianMs": statistics.median(r["totalMs"] for r in baseline["result"]), "candidateMedianMs": statistics.median(r["totalMs"] for r in candidate["result"])}, indent=2))
