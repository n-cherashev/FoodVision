"""FastAPI app: the web page and the MJPEG video stream."""

import logging
from contextlib import asynccontextmanager

import uvicorn
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, StreamingResponse
from fastapi.staticfiles import StaticFiles

from config import PROJECT_ROOT, REGIONS_CONFIG_PATH, settings
from counting.load_regions import load_zones
from database.connection import init_database
from database.repository import sync_tables
from video_processing import start_processing, stop_processing, stream_frames

FRONTEND_DIR = PROJECT_ROOT / "frontend"

logger = logging.getLogger(__name__)


def _prepare_database():
    """Create the schema and synchronize the tables before processing starts."""
    init_database()
    sync_tables([(zone["name"], zone["max"]) for zone in load_zones(REGIONS_CONFIG_PATH)])


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
    # One background processing thread for the whole server lifetime:
    # browsers only read the frames it publishes, they never start it.
    _prepare_database()
    try:
        start_processing()
    except Exception:
        # A missing model or unreadable video must not abort start-up: the
        # page still loads and reports that the stream is unavailable.
        logger.exception("Video processing could not start")
        stop_processing()
    try:
        yield
    finally:
        stop_processing()


app = FastAPI(lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.add_middleware(ConnectionCloseMiddleware)

# Static assets for the dashboard (HTML/CSS/JS in frontend/).
app.mount("/static", StaticFiles(directory=FRONTEND_DIR), name="static")


@app.get("/")
def index():
    return FileResponse(FRONTEND_DIR / "index.html")


@app.get("/stream")
def stream():
    return StreamingResponse(
        stream_frames(),
        media_type="multipart/x-mixed-replace; boundary=frame",
    )


@app.get("/api/tables")
def get_tables():
    return load_zones(REGIONS_CONFIG_PATH)


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
