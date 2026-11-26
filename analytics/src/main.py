"""FastAPI app: the web page, MJPEG video stream, and table editing API."""

import logging
from contextlib import asynccontextmanager
from pathlib import Path

import cv2
import numpy as np
import uvicorn
from fastapi import FastAPI, File, HTTPException, Response, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, StreamingResponse
from fastapi.staticfiles import StaticFiles

from config import PROJECT_ROOT, REGIONS_CONFIG_PATH, settings
from counting.load_regions import load_zones
from database.connection import init_database
from database.repository import load_active_zones, save_active_zones, sync_tables
from video_processing import (
    get_frame_at,
    get_latest_raw_frame,
    reload_processor_zones,
    set_video_path,
    start_processing,
    stop_processing,
    stream_frames,
)

FRONTEND_DIR = PROJECT_ROOT / "frontend"

logger = logging.getLogger(__name__)


def _prepare_database():
    """Create the schema and synchronize the tables before processing starts."""
    init_database()
    active_zones = load_active_zones()
    sync_tables([(zone["name"], zone["max"]) for zone in active_zones])


class ConnectionCloseMiddleware:
    """Advertise single-use connections: every response ends its connection."""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        async def send_with_close(message):
            if message["type"] == "http.response.start":
                headers = list(message.get("headers", []))
                headers.append((b"connection", b"close"))
                headers.append((b"access-control-allow-origin", b"*"))
                message = dict(message, headers=headers)
            await send(message)

        await self.app(scope, receive, send_with_close)


@asynccontextmanager
async def lifespan(app: FastAPI):
    _prepare_database()
    try:
        start_processing()
    except Exception:
        logger.exception("Video processing could not start")
        stop_processing()
    try:
        yield
    finally:
        stop_processing()


app = FastAPI(title="FoodVision Video Analytics", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.add_middleware(ConnectionCloseMiddleware)

# Static assets for the dashboard (HTML/CSS/JS in frontend/).
if FRONTEND_DIR.exists():
    app.mount("/static", StaticFiles(directory=FRONTEND_DIR), name="static")


@app.get("/")
def index():
    if (FRONTEND_DIR / "index.html").exists():
        return FileResponse(FRONTEND_DIR / "index.html")
    return {"message": "FoodVision Video Analytics Service"}


@app.get("/stream")
def stream():
    return StreamingResponse(
        stream_frames(),
        media_type="multipart/x-mixed-replace; boundary=frame",
    )


@app.get("/api/video/frame")
def get_video_frame(time: float | None = None):
    """Retrieve a single frame from the video stream or specified timestamp as JPEG."""
    frame = None
    ts = 0.0
    if time is not None:
        frame = get_frame_at(time)
        ts = time
    else:
        frame, ts = get_latest_raw_frame()
        if frame is None:
            frame = get_frame_at(0.0)
            ts = 0.0

    if frame is None:
        raise HTTPException(status_code=404, detail="Frame not available yet")

    ret, jpeg = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 90])
    if not ret:
        raise HTTPException(status_code=500, detail="Failed to encode frame")

    return Response(
        content=jpeg.tobytes(),
        media_type="image/jpeg",
        headers={
            "X-Video-Time": str(ts),
            "X-Frame-Width": str(frame.shape[1]),
            "X-Frame-Height": str(frame.shape[0]),
            "Access-Control-Expose-Headers": "X-Video-Time, X-Frame-Width, X-Frame-Height",
        },
    )


@app.post("/api/video/upload")
async def upload_video(file: UploadFile = File(...)):
    """Upload a new video file and switch playback to it."""
    out_dir = PROJECT_ROOT / "videos"
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / file.filename

    with open(out_path, "wb") as f:
        while chunk := await file.read(1024 * 1024):
            f.write(chunk)

    set_video_path(str(out_path))
    return {
        "success": True,
        "filename": file.filename,
        "video_path": str(out_path),
    }


@app.post("/api/tables/autodetect")
async def autodetect_tables(time: float | None = None, file: UploadFile | None = None):
    """Detect dining tables in the specified frame or uploaded image."""
    if file is not None:
        contents = await file.read()
        arr = np.frombuffer(contents, np.uint8)
        frame = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    elif time is not None:
        frame = get_frame_at(time)
    else:
        frame, _ = get_latest_raw_frame()
        if frame is None:
            frame = get_frame_at(0.0)

    if frame is None:
        raise HTTPException(status_code=400, detail="No frame available for detection")

    from services.table_detector import TableDetector

    detector = TableDetector()
    detected = detector.detect_tables(frame)
    h, w = frame.shape[:2]
    return {"tables": detected, "frame_width": w, "frame_height": h}


@app.get("/api/tables/zones")
@app.get("/api/tables")
def get_tables():
    """Return currently active table zones from DB."""
    return load_active_zones()


@app.post("/api/tables/reload")
def reload_tables(payload: dict | list | None = None):
    """Save updated zones to DB and hot-reload them into the video processor."""
    zones = None
    if isinstance(payload, list):
        zones = payload
    elif isinstance(payload, dict):
        zones = payload.get("tables") or payload.get("zones")

    if zones:
        save_active_zones(zones)

    reload_processor_zones(zones)
    active = load_active_zones()
    return {"success": True, "count": len(active), "zones": active}


@app.get("/api/status")
def get_status():
    return {
        "status": "running",
        "video_path": settings.VIDEO_PATH,
        "model_path": settings.MODEL_PATH,
        "show_dashboard": settings.SHOW_DASHBOARD,
    }


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=5000, timeout_keep_alive=0)
