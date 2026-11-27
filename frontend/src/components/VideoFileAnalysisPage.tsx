import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { SetupEditor } from "./SetupEditor";
import { OrdersPanel } from "./OrdersPanel";
import { uploadVideoFile, saveTableZones } from "../api";
import type { DetectedTable, OrderDetails, TableZone } from "../types";

const analyticsUrl = import.meta.env.VITE_ANALYTICS_URL || "http://localhost:5000";

interface VideoFileAnalysisPageProps {
  initialZones: TableZone[];
  orders: OrderDetails[];
  onRefreshOrders(): Promise<void> | void;
}

type Step = "select" | "layout" | "playback";

export function VideoFileAnalysisPage({ initialZones, orders, onRefreshOrders }: VideoFileAnalysisPageProps) {
  const [step, setStep] = useState<Step>("select");
  const [videoFile, setVideoFile] = useState<File | null>(null);
  const [videoUrl, setVideoUrl] = useState<string>(`${analyticsUrl}/videos/Cctv-Resturant-Demo.mp4`);
  const [videoName, setVideoName] = useState<string>("Cctv-Resturant-Demo.mp4");
  const [customZones, setCustomZones] = useState<TableZone[]>(initialZones);

  // Extracted frame from video
  const [frameUrl, setFrameUrl] = useState<string | null>(null);
  const [frameDimensions, setFrameDimensions] = useState<{ width: number; height: number }>({ width: 1280, height: 720 });
  const [frameExtractionLoading, setFrameExtractionLoading] = useState(false);

  // Playback state
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [syncingToServer, setSyncingToServer] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);

  const videoRef = useRef<HTMLVideoElement>(null);

  // Cleanup object URLs on unmount
  useEffect(() => {
    return () => {
      if (videoUrl.startsWith("blob:")) {
        URL.revokeObjectURL(videoUrl);
      }
    };
  }, [videoUrl]);

  async function extractFrameFromVideoUrl(url: string): Promise<{ url: string; width: number; height: number }> {
    return new Promise((resolve, reject) => {
      const video = document.createElement("video");
      video.crossOrigin = "anonymous";
      video.preload = "auto";
      video.muted = true;
      video.playsInline = true;
      video.src = url;

      video.onloadedmetadata = () => {
        video.currentTime = Math.min(0.5, (video.duration || 1) / 2);
      };

      video.onseeked = () => {
        const canvas = document.createElement("canvas");
        const w = video.videoWidth || 1280;
        const h = video.videoHeight || 720;
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext("2d");
        if (ctx) {
          ctx.drawImage(video, 0, 0, w, h);
          const dataUrl = canvas.toDataURL("image/jpeg", 0.92);
          resolve({ url: dataUrl, width: w, height: h });
        } else {
          reject(new Error("Не удалось получить контекст канваса"));
        }
      };

      video.onerror = () => {
        reject(new Error("Не удалось загрузить стоп-кадр из видео"));
      };
    });
  }

  async function handleFileSelected(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;

    if (videoUrl.startsWith("blob:")) {
      URL.revokeObjectURL(videoUrl);
    }

    const objUrl = URL.createObjectURL(file);
    setVideoFile(file);
    setVideoUrl(objUrl);
    setVideoName(file.name);
    setSyncMessage(null);

    // Extract first frame for the layout editor
    setFrameExtractionLoading(true);
    try {
      const extracted = await extractFrameFromVideoUrl(objUrl);
      setFrameUrl(extracted.url);
      setFrameDimensions({ width: extracted.width, height: extracted.height });
      setStep("layout");
    } catch {
      // Fallback: still go to layout step
      setStep("layout");
    } finally {
      setFrameExtractionLoading(false);
    }
  }

  async function handleLoadDemoVideo() {
    const demo = `${analyticsUrl}/videos/Cctv-Resturant-Demo.mp4`;
    setVideoFile(null);
    setVideoUrl(demo);
    setVideoName("Cctv-Resturant-Demo.mp4");
    setSyncMessage(null);

    setFrameExtractionLoading(true);
    try {
      const extracted = await extractFrameFromVideoUrl(demo);
      setFrameUrl(extracted.url);
      setFrameDimensions({ width: extracted.width, height: extracted.height });
      setStep("layout");
    } catch {
      setStep("layout");
    } finally {
      setFrameExtractionLoading(false);
    }
  }

  function handleSaveCustomZones(newZones: TableZone[]) {
    setCustomZones(newZones);
    setStep("playback");
    setSyncMessage(`Разметка для видео «${videoName}» сохранена (${newZones.length} столов).`);
  }

  async function handleSyncWithServerAnalytics() {
    setSyncingToServer(true);
    setSyncMessage("Синхронизация видео и разметки с сервером AI-аналитики...");
    try {
      if (videoFile) {
        await uploadVideoFile(videoFile);
      }
      await saveTableZones(customZones);
      setSyncMessage("✅ Видео и персональная разметка столов успешно загружены на сервер и активны!");
    } catch (err) {
      setSyncMessage(`❌ Ошибка синхронизации: ${err instanceof Error ? err.message : String(err)}`);
    } finally {
      setSyncingToServer(false);
    }
  }

  // Convert custom zones to DetectedTable format for Video player overlay
  const detectedTablesForPlayback: DetectedTable[] = customZones.map((z) => {
    const xs = z.displayPolygon.map((p) => p[0]);
    const ys = z.displayPolygon.map((p) => p[1]);
    const minX = xs.length ? Math.min(...xs) : 0;
    const maxX = xs.length ? Math.max(...xs) : 100;
    const minY = ys.length ? Math.min(...ys) : 0;
    const maxY = ys.length ? Math.max(...ys) : 100;

    return {
      id: z.name || z.id,
      bounds: {
        x: frameDimensions.width > 0 ? minX / frameDimensions.width : minX / 1280,
        y: frameDimensions.height > 0 ? minY / frameDimensions.height : minY / 720,
        width: frameDimensions.width > 0 ? (maxX - minX) / frameDimensions.width : (maxX - minX) / 1280,
        height: frameDimensions.height > 0 ? (maxY - minY) / frameDimensions.height : (maxY - minY) / 720,
      },
      segments: [
        {
          startSeconds: 0,
          endSeconds: 999999,
          state: "available",
        },
      ],
    };
  });

  return (
    <div className="video-file-page-container">
      {/* Step navigation bar */}
      <div className="file-workflow-bar">
        <div className="workflow-title-block">
          <h2>🎬 Анализ видеофайла со своей разметкой</h2>
          <p className="workflow-sub">
            Загрузите свой видеофайл, настройте для него индивидуальные контуры столов и просматривайте видеозапись.
          </p>
        </div>

        <div className="step-pill-group">
          <button
            type="button"
            className={`step-pill ${step === "select" ? "active" : ""}`}
            onClick={() => setStep("select")}
          >
            1️⃣ Выбор видео
          </button>
          <button
            type="button"
            className={`step-pill ${step === "layout" ? "active" : ""}`}
            onClick={() => setStep("layout")}
          >
            2️⃣ Своя разметка столов ({customZones.length})
          </button>
          <button
            type="button"
            className={`step-pill ${step === "playback" ? "active" : ""}`}
            onClick={() => setStep("playback")}
          >
            3️⃣ Просмотр с разметкой
          </button>
        </div>
      </div>

      {syncMessage && (
        <div className={`workflow-alert ${syncMessage.startsWith("✅") ? "success" : syncMessage.startsWith("❌") ? "error" : "info"}`}>
          {syncMessage}
        </div>
      )}

      {/* STEP 1: Select / Upload File */}
      {step === "select" && (
        <div className="file-selection-panel">
          <div className="file-dropzone-box">
            <span className="dropzone-icon">📁</span>
            <h3>Выберите видеозапись ресторана</h3>
            <p>Поддерживаются форматы MP4, MOV, MKV, AVI, WebM с любой камеры видеонаблюдения.</p>

            <div className="file-action-buttons">
              <label className="primary-button pick-file-btn">
                📂 Выбрать видео с компьютера
                <input
                  type="file"
                  accept="video/*"
                  style={{ display: "none" }}
                  onChange={handleFileSelected}
                />
              </label>

              <button
                type="button"
                className="secondary-button"
                onClick={handleLoadDemoVideo}
                disabled={frameExtractionLoading}
              >
                🎥 Использовать демо-видео ресторана
              </button>
            </div>

            {frameExtractionLoading && (
              <p className="frame-extraction-status">⏳ Извлечение первого кадра для редактора разметки...</p>
            )}
          </div>

          <div className="current-file-meta-card">
            <h4>Активный файл:</h4>
            <p><strong>{videoName}</strong></p>
            {videoFile && (
              <p className="file-size-info">
                Размер: {(videoFile.size / (1024 * 1024)).toFixed(1)} МБ
              </p>
            )}
            <button
              type="button"
              className="primary-button proceed-btn"
              onClick={() => setStep("layout")}
            >
              Далее: Настроить разметку столов для этого видео ➡️
            </button>
          </div>
        </div>
      )}

      {/* STEP 2: Custom Layout Editor for this video */}
      {step === "layout" && (
        <div className="file-layout-editor-pane">
          <div className="layout-editor-actions-strip">
            <button
              type="button"
              className="secondary-button"
              onClick={() => setStep("select")}
            >
              ← Сменить видеофайл ({videoName})
            </button>

            <button
              type="button"
              className="primary-button"
              onClick={() => setStep("playback")}
            >
              Перейти к просмотру с этой разметкой ➡️
            </button>
          </div>

          <SetupEditor
            initialFrameUrl={frameUrl}
            initialFrameDimensions={frameDimensions}
            initialZones={customZones}
            onSaveZones={handleSaveCustomZones}
            title={`📐 Своя разметка столов для «${videoName}»`}
            subtitle="Настройте столы конкретно для этого видео. Кликайте по ребрам для добавления точек (скругление круглых столов), двигайте вершины, удаляйте правым кликом."
            saveButtonLabel="💾 Применить разметку к видео"
            showSaveDbButton={false}
            showVideoUpload={false}
          />
        </div>
      )}

      {/* STEP 3: Video Playback with Custom Layout & Orders */}
      {step === "playback" && (
        <div className="file-playback-pane">
          <div className="playback-top-controls">
            <div className="playback-file-info">
              <span>Видеозапись: <strong>{videoName}</strong></span>
              <span className="zones-count-pill">{customZones.length} столов в разметке</span>
            </div>

            <div className="playback-actions">
              <button
                type="button"
                className="secondary-button"
                onClick={() => setStep("layout")}
              >
                ✏️ Редактировать разметку столов
              </button>

              <button
                type="button"
                className="secondary-button primary-accent"
                onClick={handleSyncWithServerAnalytics}
                disabled={syncingToServer}
                title="Отправить это видео и разметку на сервер для live AI-трансляции"
              >
                {syncingToServer ? "⏳ Синхронизация..." : "☁️ Загрузить в live AI-стрим"}
              </button>
            </div>
          </div>

          {/* Video Player with Custom Polygons Overlay */}
          <div className="video-frame">
            <video
              ref={videoRef}
              key={videoUrl}
              src={videoUrl}
              controls
              preload="metadata"
              onTimeUpdate={(e) => setCurrentTime(e.currentTarget.currentTime)}
            />

            <div className="table-overlay" aria-hidden="true">
              {detectedTablesForPlayback.map((table, index) => {
                const tableOrders = orders.filter((o) => o.tableId === table.id);
                const activeOrder = tableOrders.find(
                  (o) => o.status === "Open" || o.status === 0 || o.status === "Paid" || o.status === 1
                );

                return (
                  <div
                    className="table-marker state-available"
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
                      <small className="state-pill available-pill">✅ Свободен</small>
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

          {/* Orders Section for this video file */}
          <OrdersPanel
            sessionId={`file-${videoName}`}
            tables={detectedTablesForPlayback}
            orders={orders}
            currentTime={currentTime}
            onSeek={(s) => {
              if (videoRef.current) {
                videoRef.current.currentTime = s;
                setCurrentTime(s);
              }
            }}
            onRefreshOrders={onRefreshOrders}
          />
        </div>
      )}
    </div>
  );
}
