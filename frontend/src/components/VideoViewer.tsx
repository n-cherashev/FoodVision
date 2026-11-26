import { forwardRef, useState } from "react";
import type { DetectedTable, OrderDetails } from "../types";

const analyticsStreamUrl = import.meta.env.VITE_ANALYTICS_URL || "http://localhost:5000";

interface VideoViewerProps {
  source: string;
  tables: DetectedTable[];
  orders?: OrderDetails[];
  currentTime?: number;
  onTimeUpdate?(currentTime: number): void;
}

export const VideoViewer = forwardRef<HTMLVideoElement, VideoViewerProps>(function VideoViewer(
  { source, tables, orders = [], currentTime = 0, onTimeUpdate },
  ref,
) {
  const [viewMode, setViewMode] = useState<"video" | "stream">("video");
  const [streamError, setStreamError] = useState(false);

  return (
    <div className="viewer-wrapper">
      <div className="viewer-mode-bar">
        <div className="mode-toggle-group">
          <button
            type="button"
            className={`mode-btn ${viewMode === "video" ? "active" : ""}`}
            onClick={() => setViewMode("video")}
          >
            🎬 Видеозапись с разметкой
          </button>
          <button
            type="button"
            className={`mode-btn ${viewMode === "stream" ? "active" : ""}`}
            onClick={() => {
              setViewMode("stream");
              setStreamError(false);
            }}
          >
            📡 AI-трансляция (Supervision + Дашборд)
          </button>
        </div>
        {viewMode === "stream" && (
          <span className="live-pill">
            <span className="live-dot" /> LIVE MJPEG
          </span>
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
            <div className="ai-stream-view">
              <img
                src={`${analyticsStreamUrl}/stream`}
                alt="AI Supervision Stream with table polygons & live dashboard"
                className="ai-stream-img"
                onError={() => setStreamError(true)}
              />
              <div className="stream-footer-info">
                <span>🟢 YOLO Pose · StandingFilter (COCO-17) · Supervision PolygonAnnotator · Live Dashboard</span>
              </div>
            </div>
          )}
        </div>
      ) : (
        <div className="video-frame">
          <video
            ref={ref}
            src={source}
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
                    Стол {index + 1}
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
      )}
    </div>
  );
});
