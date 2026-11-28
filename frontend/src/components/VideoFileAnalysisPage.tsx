import { useEffect, useRef, useState, type ChangeEvent } from "react";
import { SetupEditor } from "./SetupEditor";
import { OrdersPanel } from "./OrdersPanel";
import { RestaurantStatusPanel } from "./RestaurantStatusPanel";
import { uploadVideoFile, selectVideoFile, saveTableZones, fetchVideoList } from "../api";
import type { DetectedTable, OrderDetails, TableZone } from "../types";

const analyticsUrl = import.meta.env.VITE_ANALYTICS_URL || "http://localhost:5000";

interface VideoFileAnalysisPageProps {
  initialZones: TableZone[];
  orders: OrderDetails[];
  onRefreshOrders(): Promise<void> | void;
}

type Step = "select" | "layout" | "playback";
type StreamLayout = "split" | "video_only" | "status_only";

export function VideoFileAnalysisPage({ initialZones, orders, onRefreshOrders }: VideoFileAnalysisPageProps) {
  const [step, setStep] = useState<Step>("select");
  const [videoFile, setVideoFile] = useState<File | null>(null);
  const [videoUrl, setVideoUrl] = useState<string>(`${analyticsUrl}/videos/Cctv-Resturant-Demo.mp4`);
  const [videoName, setVideoName] = useState<string>("Cctv-Resturant-Demo.mp4");
  const [availableVideos, setAvailableVideos] = useState<string[]>([]);
  const [customZones, setCustomZones] = useState<TableZone[]>(initialZones);

  // Extracted frame from video for layout editor
  const [frameUrl, setFrameUrl] = useState<string | null>(null);
  const [frameDimensions, setFrameDimensions] = useState<{ width: number; height: number }>({ width: 1280, height: 720 });
  const [frameExtractionLoading, setFrameExtractionLoading] = useState(false);

  // Playback & stream state
  const [viewMode, setViewMode] = useState<"ai_stream" | "raw_video">("ai_stream");
  const [streamLayout, setStreamLayout] = useState<StreamLayout>("split");
  const [streamKey, setStreamKey] = useState<number>(() => Date.now());
  const [streamError, setStreamError] = useState(false);
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [syncingToServer, setSyncingToServer] = useState(false);
  const [syncMessage, setSyncMessage] = useState<string | null>(null);

  const videoRef = useRef<HTMLVideoElement>(null);

  // Load available server videos on mount
  useEffect(() => {
    void fetchVideoList().then((list) => {
      if (list && list.length > 0) {
        setAvailableVideos(list);
      }
    });
  }, []);

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

    setFrameExtractionLoading(true);
    try {
      const extracted = await extractFrameFromVideoUrl(objUrl);
      setFrameUrl(extracted.url);
      setFrameDimensions({ width: extracted.width, height: extracted.height });
      setStep("layout");
    } catch {
      setStep("layout");
    } finally {
      setFrameExtractionLoading(false);
    }
  }

  async function handleSelectExistingVideo(selectedFilename: string) {
    if (videoUrl.startsWith("blob:")) {
      URL.revokeObjectURL(videoUrl);
    }

    const url = `${analyticsUrl}/videos/${selectedFilename}`;
    setVideoFile(null);
    setVideoUrl(url);
    setVideoName(selectedFilename);
    setSyncMessage(null);

    setFrameExtractionLoading(true);
    try {
      const extracted = await extractFrameFromVideoUrl(url);
      setFrameUrl(extracted.url);
      setFrameDimensions({ width: extracted.width, height: extracted.height });
      setStep("layout");
    } catch {
      setStep("layout");
    } finally {
      setFrameExtractionLoading(false);
    }
  }

  async function handleApplyZonesAndStartAnalysis(newZones: TableZone[]) {
    setCustomZones(newZones);
    setSyncingToServer(true);
    setSyncMessage(`Отправка видео «${videoName}» и персональной разметки (${newZones.length} столов) в сервис видеоаналитики Python...`);
    try {
      if (videoFile) {
        await uploadVideoFile(videoFile);
      } else {
        await selectVideoFile(videoName);
      }

      await saveTableZones(newZones);
      setStreamKey(Date.now());
      setStreamError(false);
      setStep("playback");
      setSyncMessage(`✅ Видео «${videoName}» и разметка столов (${newZones.length} столов) запущены в Python AI-сервисе!`);
    } catch (err) {
      setSyncMessage(`⚠️ Разметка сохранена локально, но при синхронизации с сервисом аналитики возникло предупреждение: ${err instanceof Error ? err.message : String(err)}`);
      setStep("playback");
    } finally {
      setSyncingToServer(false);
    }
  }

  async function handleRestartStream() {
    setSyncingToServer(true);
    setSyncMessage(`Перезапуск видео «${videoName}» в сервисе аналитики...`);
    try {
      if (videoFile) {
        await uploadVideoFile(videoFile);
      } else {
        await selectVideoFile(videoName);
      }
      await saveTableZones(customZones);
      setStreamKey(Date.now());
      setStreamError(false);
      setSyncMessage(`✅ Видео «${videoName}» успешно перезапущено!`);
    } catch (err) {
      setSyncMessage(`❌ Ошибка перезапуска: ${err instanceof Error ? err.message : String(err)}`);
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
            Загрузите свой видеофайл, настройте для него персональную разметку столов и смотрите видеопоток в сервисе AI-аналитики (Python :5000).
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
            3️⃣ AI-Анализ видеопотока
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
            <p>Загрузите видеозапись с компьютера или выберите демонстрационное видео с сервера.</p>

            <div className="file-action-buttons">
              <label className="primary-button pick-file-btn">
                📂 Загрузить видео с компьютера
                <input
                  type="file"
                  accept="video/*"
                  style={{ display: "none" }}
                  onChange={handleFileSelected}
                />
              </label>

              {availableVideos.length > 0 ? (
                <div className="server-videos-select-group">
                  <span className="server-videos-label">Или выберите видео с сервера:</span>
                  <div className="server-videos-btn-row">
                    {availableVideos.map((fname) => (
                      <button
                        key={fname}
                        type="button"
                        className={`secondary-button ${videoName === fname ? "active-video-btn" : ""}`}
                        onClick={() => handleSelectExistingVideo(fname)}
                        disabled={frameExtractionLoading}
                      >
                        🎥 {fname}
                      </button>
                    ))}
                  </div>
                </div>
              ) : (
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => handleSelectExistingVideo("Cctv-Resturant-Demo.mp4")}
                  disabled={frameExtractionLoading}
                >
                  🎥 Cctv-Resturant-Demo.mp4
                </button>
              )}
            </div>

            {frameExtractionLoading && (
              <p className="frame-extraction-status">⏳ Извлечение кадра для редактора разметки...</p>
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
              disabled={syncingToServer}
              onClick={() => void handleApplyZonesAndStartAnalysis(customZones)}
            >
              {syncingToServer ? "⏳ Применение к AI-сервису..." : "Запустить AI-анализ с этой разметкой ➡️"}
            </button>
          </div>

          <SetupEditor
            videoSource={videoUrl}
            initialFrameUrl={frameUrl}
            initialFrameDimensions={frameDimensions}
            initialZones={customZones}
            onSaveZones={(newZones) => void handleApplyZonesAndStartAnalysis(newZones)}
            title={`📐 Персональная разметка столов для «${videoName}»`}
            subtitle="Проматывайте видео ползунком ниже, чтобы выбрать идеальный кадр для разметки. Двигайте вершины, добавляйте грани (скругление столов) или используйте автодетект YOLO."
            saveButtonLabel="💾 Применить разметку и запустить AI-анализ"
            showSaveDbButton={false}
            showVideoUpload={false}
          />
        </div>
      )}

      {/* STEP 3: Video Stream & Analytics (Powered by Python Analytics :5000) */}
      {step === "playback" && (
        <div className="file-playback-pane">
          {/* Top Control Bar */}
          <div className="playback-top-controls">
            <div className="playback-file-info">
              <span className="live-pulse" />
              <span>Видео: <strong>{videoName}</strong></span>
              <span className="zones-count-pill">{customZones.length} столов в разметке</span>
              <span className="active-service-badge">⚡ Python AI Analytics (:5000)</span>
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
                onClick={() => void handleRestartStream()}
                disabled={syncingToServer}
                title="Перезапустить видео и обновить видеопоток с текущей разметкой"
              >
                {syncingToServer ? "⏳ Обновление..." : "🔄 Перезапустить видеопоток"}
              </button>
            </div>
          </div>

          {/* Mode Switcher Bar */}
          <div className="viewer-mode-bar">
            <div className="mode-toggle-group">
              <button
                type="button"
                className={`mode-btn ${viewMode === "ai_stream" ? "active" : ""}`}
                onClick={() => {
                  setViewMode("ai_stream");
                  setStreamError(false);
                }}
              >
                📡 Поток AI-видеоаналитики (Real-Time)
              </button>
              <button
                type="button"
                className={`mode-btn ${viewMode === "raw_video" ? "active" : ""}`}
                onClick={() => setViewMode("raw_video")}
              >
                🎥 Исходный видеоплеер (HTML5)
              </button>
            </div>

            {viewMode === "ai_stream" && (
              <div className="stream-layout-toggles">
                <span className="layout-toggle-label">Режим отображения:</span>
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
                  📺 Только AI-видео
                </button>
                <button
                  type="button"
                  className={`layout-btn ${streamLayout === "status_only" ? "active" : ""}`}
                  onClick={() => setStreamLayout("status_only")}
                  title="Только аналитическая панель статуса"
                >
                  📊 Только статус
                </button>
              </div>
            )}
          </div>

          {/* Main Display: AI Stream or Raw Video Player */}
          {viewMode === "ai_stream" ? (
            <div className="stream-frame-container">
              <div className={`ai-stream-split-layout layout-${streamLayout}`}>
                {(streamLayout === "split" || streamLayout === "video_only") && (
                  <div className="stream-video-pane">
                    <div className="stream-video-header">
                      <span className="video-stream-badge">
                        🟢 Видеопоток аналитики: <strong>{videoName}</strong>
                      </span>
                      <span className="stream-fps-badge">
                        MJPEG · Supervision · {customZones.length} столов
                      </span>
                    </div>

                    {!streamError ? (
                      <img
                        key={streamKey}
                        src={`${analyticsUrl}/stream/video?t=${streamKey}`}
                        alt="AI Video Stream"
                        className="ai-stream-img"
                        onError={() => setStreamError(true)}
                      />
                    ) : (
                      <div className="stream-fallback">
                        <p>⚠️ Сервер видеоаналитики ({analyticsUrl}) не отвечает или перезапускается.</p>
                        <button
                          type="button"
                          className="secondary-button"
                          onClick={() => {
                            setStreamError(false);
                            setStreamKey(Date.now());
                          }}
                        >
                          🔄 Повторить подключение
                        </button>
                      </div>
                    )}

                    <div className="stream-footer-info">
                      <span>Камера / Файл: <strong>{videoName}</strong></span>
                      <span>Модель: YOLO11 Pose + Детекция столов</span>
                    </div>
                  </div>
                )}

                {(streamLayout === "split" || streamLayout === "status_only") && (
                  <div className="stream-status-pane">
                    <RestaurantStatusPanel />
                  </div>
                )}
              </div>
            </div>
          ) : (
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

          {/* Orders Management for this video */}
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
