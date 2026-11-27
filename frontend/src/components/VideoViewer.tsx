import { forwardRef, useEffect, useState, type ChangeEvent } from "react";
import { uploadVideoFile, fetchCurrentVideoInfo } from "../api";
import { RestaurantStatusPanel } from "./RestaurantStatusPanel";
import type { DetectedTable, OrderDetails } from "../types";

const analyticsStreamUrl = import.meta.env.VITE_ANALYTICS_URL || "http://localhost:5000";

interface VideoViewerProps {
  source?: string;
  tables: DetectedTable[];
  orders?: OrderDetails[];
  currentTime?: number;
  onTimeUpdate?(currentTime: number): void;
}

type StreamLayout = "split" | "video_only" | "status_only";

export const VideoViewer = forwardRef<HTMLVideoElement, VideoViewerProps>(function VideoViewer(
  { source, tables, orders = [], currentTime = 0, onTimeUpdate },
  ref,
) {
  const [viewMode, setViewMode] = useState<"video" | "stream">("stream");
  const [streamLayout, setStreamLayout] = useState<StreamLayout>("split");
  const [streamError, setStreamError] = useState(false);

  // Local video state
  const [videoSource, setVideoSource] = useState<string>(
    source && !source.includes("/stream") ? source : `${analyticsStreamUrl}/videos/Cctv-Resturant-Demo.mp4`
  );
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [activeFileName, setActiveFileName] = useState<string>("Cctv-Resturant-Demo.mp4");
  const [uploading, setUploading] = useState(false);
  const [uploadStatus, setUploadStatus] = useState<string | null>(null);

  useEffect(() => {
    // Try to load current video name from analytics server
    void fetchCurrentVideoInfo()
      .then((info) => {
        if (info.filename) {
          setActiveFileName(info.filename);
        }
        if (info.url && (!source || source.includes("/stream"))) {
          setVideoSource(info.url);
        }
      })
      .catch(() => {});
  }, [source]);

  function handleFileChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;

    setSelectedFile(file);
    setActiveFileName(file.name);
    const localUrl = URL.createObjectURL(file);
    setVideoSource(localUrl);
    setUploadStatus(`Выбран локальный файл «${file.name}». Вы можете воспроизвести его или загрузить на сервер.`);
  }

  async function handleUploadToServer() {
    if (!selectedFile) return;
    setUploading(true);
    setUploadStatus(`Загрузка файла «${selectedFile.name}» на сервер аналитики...`);
    try {
      await uploadVideoFile(selectedFile);
      setUploadStatus(`✅ Файл «${selectedFile.name}» успешно загружен на сервер аналитики и запущен в AI-обработку!`);
    } catch (err) {
      setUploadStatus(`❌ Ошибка загрузки: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setUploading(false);
    }
  }

  function handleLoadServerDemo() {
    const demoUrl = `${analyticsStreamUrl}/videos/Cctv-Resturant-Demo.mp4`;
    setVideoSource(demoUrl);
    setActiveFileName("Cctv-Resturant-Demo.mp4");
    setSelectedFile(null);
    setUploadStatus("Загружено демонстрационное видео ресторана с сервера.");
  }

  return (
    <div className="viewer-wrapper">
      <div className="viewer-mode-bar">
        <div className="mode-toggle-group">
          <button
            type="button"
            className={`mode-btn ${viewMode === "stream" ? "active" : ""}`}
            onClick={() => {
              setViewMode("stream");
              setStreamError(false);
            }}
          >
            📡 AI-трансляция (Real-Time)
          </button>
          <button
            type="button"
            className={`mode-btn ${viewMode === "video" ? "active" : ""}`}
            onClick={() => setViewMode("video")}
          >
            🎬 Видеозапись с разметкой
          </button>
        </div>

        {viewMode === "stream" && (
          <div className="stream-layout-toggles">
            <span className="layout-toggle-label">Отображение:</span>
            <button
              type="button"
              className={`layout-btn ${streamLayout === "split" ? "active" : ""}`}
              onClick={() => setStreamLayout("split")}
              title="Показать видеоролик и ресторанный статус раздельно"
            >
              🖥️ Видео + Статус
            </button>
            <button
              type="button"
              className={`layout-btn ${streamLayout === "video_only" ? "active" : ""}`}
              onClick={() => setStreamLayout("video_only")}
              title="Полноэкранный видеоролик без боковых панелей"
            >
              📺 Только видео
            </button>
            <button
              type="button"
              className={`layout-btn ${streamLayout === "status_only" ? "active" : ""}`}
              onClick={() => setStreamLayout("status_only")}
              title="Показать только ресторанный статус и таймеры столов"
            >
              📊 Только статус
            </button>
            <span className="live-pill">
              <span className="live-dot" /> LIVE MJPEG
            </span>
          </div>
        )}
      </div>

      {viewMode === "stream" ? (
        <div className="stream-frame-container">
          {streamError ? (
            <div className="stream-fallback">
              <p>⚠️ AI-трансляция Supervision временно недоступна (порт 5000).</p>
              <button
                type="button"
                className="secondary-button"
                onClick={() => setStreamError(false)}
              >
                Повторить подключение
              </button>
            </div>
          ) : (
            <div className={`ai-stream-split-layout layout-${streamLayout}`}>
              {/* Separate Video Stream */}
              {(streamLayout === "split" || streamLayout === "video_only") && (
                <div className="stream-video-pane">
                  <div className="stream-video-header">
                    <span className="video-stream-badge">🎥 Видеопоток камеры (Чистый 16:9 + AI-разметка)</span>
                    <span className="stream-fps-badge">25 FPS</span>
                  </div>
                  <img
                    src={`${analyticsStreamUrl}/stream/video`}
                    alt="Чистый AI видеоролик зала с полигонами столов"
                    className="ai-stream-img"
                    onError={() => setStreamError(true)}
                  />
                  <div className="stream-footer-info">
                    <span>🟢 YOLO Pose · StandingFilter · Supervision PolygonAnnotator · Раздельная трансляция</span>
                  </div>
                </div>
              )}

              {/* Separate Restaurant Status Panel */}
              {(streamLayout === "split" || streamLayout === "status_only") && (
                <div className="stream-status-pane">
                  <RestaurantStatusPanel />
                </div>
              )}
            </div>
          )}
        </div>
      ) : (
        /* Mode 2: Playback of uploaded video with table overlays */
        <div className="video-playback-container">
          {/* Upload Toolbar on Video Playback Tab */}
          <div className="video-upload-toolbar">
            <div className="toolbar-left">
              <label className="secondary-button file-picker-label">
                📁 Выбрать видеоролик
                <input
                  type="file"
                  accept="video/*"
                  style={{ display: "none" }}
                  onChange={handleFileChange}
                  disabled={uploading}
                />
              </label>

              {selectedFile && (
                <button
                  type="button"
                  className="primary-button upload-to-ai-btn"
                  onClick={handleUploadToServer}
                  disabled={uploading}
                >
                  {uploading ? "⏳ Загрузка..." : "⬆️ Загрузить на сервер аналитики"}
                </button>
              )}

              <button
                type="button"
                className="secondary-button"
                onClick={handleLoadServerDemo}
                disabled={uploading}
              >
                🎥 Демо ресторана
              </button>
            </div>

            <div className="toolbar-right">
              <span className="active-video-badge">
                Текущее видео: <strong>{activeFileName}</strong>
              </span>
            </div>
          </div>

          {uploadStatus && (
            <div className={`upload-status-alert ${uploadStatus.startsWith("✅") ? "success" : uploadStatus.startsWith("❌") ? "error" : "info"}`}>
              {uploadStatus}
            </div>
          )}

          <div className="video-frame">
            <video
              ref={ref}
              key={videoSource}
              src={videoSource}
              controls
              preload="metadata"
              onTimeUpdate={(e) => onTimeUpdate?.(e.currentTarget.currentTime)}
            />
            <div className="table-overlay" aria-hidden="true">
              {tables.map((table, index) => {
                const tableOrders = orders.filter((o) => o.tableId === table.id);
                const activeOrder = tableOrders.find(
                  (o) => o.status === "Open" || o.status === 0 || o.status === "Paid" || o.status === 1
                );

                const currentSeg = table.segments?.find(
                  (s) => currentTime >= s.startSeconds && currentTime <= s.endSeconds
                );
                const state = currentSeg ? currentSeg.state : "available";
                const isApproaching = table.serviceMetrics?.approaches.some(
                  (a) => currentTime >= a.startSeconds && currentTime <= a.endSeconds
                );

                let markerClass = `table-marker state-${state.replaceAll("_", "-")}`;
                if (activeOrder) markerClass += " has-active-order";
                if (isApproaching) markerClass += " waiter-approaching";

                return (
                  <div
                    className={markerClass}
                    key={table.id}
                    style={{
                      left: `${table.bounds.x * 100}%`,
                      top: `${table.bounds.y * 100}%`,
                      width: `${table.bounds.width * 100}%`,
                      height: `${table.bounds.height * 100}%`,
                    }}
                  >
                    <span>
                      {table.id || `Стол ${index + 1}`}
                      {state === "occupied" && <small className="state-pill occupied-pill">👥 Занят</small>}
                      {state === "occupied_with_items" && <small className="state-pill items-pill">🎒 Вещи</small>}
                      {state === "available" && <small className="state-pill available-pill">✅ Свободен</small>}
                      {isApproaching && (
                        <strong className="waiter-approaching-badge">
                          🤵 Официант
                        </strong>
                      )}
                      {activeOrder && (
                        <strong className="table-active-order-badge">
                          📋 {activeOrder.orderNumber || "Заказ"} ({activeOrder.totalAmount} ₽)
                        </strong>
                      )}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
});
