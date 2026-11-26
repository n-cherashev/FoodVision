"""Auto-detection of dining tables in a video frame using YOLO."""

import logging
from pathlib import Path
import cv2
import numpy as np
from ultralytics import YOLO

from config import PROJECT_ROOT, settings

logger = logging.getLogger(__name__)


class TableDetector:
    """Detects tables and generates initial display and seating polygons."""

    def __init__(self, model_path: str | None = None):
        if model_path is None:
            model_path = getattr(settings, "TABLE_MODEL_PATH", "models/yolo11x.onnx")

        full_path = Path(model_path)
        if not full_path.is_absolute():
            full_path = PROJECT_ROOT / full_path

        if not full_path.exists():
            # Fallback to standard pretrained yolo11s.pt if onnx is missing
            model_path = "yolo11s.pt"
        else:
            model_path = str(full_path)

        try:
            self.model = YOLO(model_path, task="detect")
            logger.info("TableDetector loaded model from %s", model_path)
        except Exception as e:
            logger.warning("Failed to load %s (%s), falling back to yolo11s.pt", model_path, e)
            self.model = YOLO("yolo11s.pt", task="detect")

    def detect_tables(self, frame: np.ndarray, conf: float = 0.15) -> list[dict]:
        """Detect dining tables in frame and return suggested polygons."""
        h, w = frame.shape[:2]

        # COCO class 60 is 'dining table'
        try:
            results = self.model.predict(
                frame,
                classes=[60],
                conf=conf,
                verbose=False,
            )[0]
            boxes = results.boxes.xyxy.cpu().numpy()
            confidences = results.boxes.conf.cpu().numpy()
        except Exception as e:
            logger.error("Error running table prediction: %s", e)
            boxes = []
            confidences = []

        # Sort tables by confidence descending for NMS
        sorted_indices = []
        if len(boxes) > 0:
            order = np.argsort(-confidences)
            keep = []
            for i in order:
                box_i = boxes[i]
                overlap = False
                for k in keep:
                    box_k = boxes[k]
                    ix1 = max(box_i[0], box_k[0])
                    iy1 = max(box_i[1], box_k[1])
                    ix2 = min(box_i[2], box_k[2])
                    iy2 = min(box_i[3], box_k[3])
                    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
                    area_i = (box_i[2] - box_i[0]) * (box_i[3] - box_i[1])
                    area_k = (box_k[2] - box_k[0]) * (box_k[3] - box_k[1])
                    union = area_i + area_k - inter
                    iou = inter / max(1, union)
                    if iou > 0.30:
                        overlap = True
                        break
                if not overlap:
                    keep.append(i)

            # Sort kept boxes naturally by (y, x)
            centers = [(boxes[i][1] * 2 + boxes[i][0]) for i in keep]
            sorted_indices = [keep[idx] for idx in np.argsort(centers)]

        detected_zones = []
        for idx, i in enumerate(sorted_indices):
            x1, y1, x2, y2 = [int(v) for v in boxes[i]]
            score = float(confidences[i])

            # Filter out tiny noise boxes
            bw, bh = x2 - x1, y2 - y1
            if bw < 40 or bh < 30:
                continue

            name = f"T-{idx + 1:02d}"

            # Table display polygon (4 corner vertices of tabletop)
            display_polygon = [
                [x1, y1],
                [x2, y1],
                [x2, y2],
                [x1, y2],
            ]

            # Expanded seating / counting polygon around table (chairs)
            margin_x = max(25, int(0.25 * bw))
            margin_y = max(25, int(0.30 * bh))
            cx1 = max(0, x1 - margin_x)
            cy1 = max(0, y1 - margin_y)
            cx2 = min(w, x2 + margin_x)
            cy2 = min(h, y2 + margin_y)

            counting_polygon = [
                [cx1, cy1],
                [cx2, cy1],
                [cx2, cy2],
                [cx1, cy2],
            ]

            # Approximate capacity based on size
            capacity = 4
            if bw * bh > 120000:
                capacity = 6
            elif bw * bh < 25000:
                capacity = 2

            detected_zones.append({
                "id": name,
                "name": name,
                "max": capacity,
                "capacity": capacity,
                "confidence": round(score, 2),
                "display_polygon": display_polygon,
                "counting_polygon": counting_polygon,
            })

        return detected_zones
