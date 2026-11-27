import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { PolygonPoint, TableZone } from "../types";
import { autoDetectTables, fetchCurrentFrame, fetchTableZones, saveTableZones, uploadVideoFile } from "../api";

interface DragTarget {
  tableIndex: number;
  layer: "display" | "counting";
  pointIndex: number;
}

export interface SetupEditorProps {
  videoSource?: string | null;
  initialFrameUrl?: string | null;
  initialFrameDimensions?: { width: number; height: number };
  initialZones?: TableZone[];
  onSaveZones?(zones: TableZone[]): void;
  title?: string;
  subtitle?: string;
  saveButtonLabel?: string;
  showSaveDbButton?: boolean;
  showVideoUpload?: boolean;
  onRefreshFrameRequest?(): Promise<{ url: string; width: number; height: number }> | void;
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "00:00";
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`;
}

export function SetupEditor({
  videoSource,
  initialFrameUrl,
  initialFrameDimensions,
  initialZones,
  onSaveZones,
  title = "📐 Интерактивный редактор столов (Геозоны)",
  subtitle = "Кликните по линии стола, чтобы добавить точку (скругление для круглых столов). Перетаскивайте вершины мышкой. Правый клик — удалить точку.",
  saveButtonLabel = "💾 Сохранить разметку",
  showSaveDbButton = true,
  showVideoUpload = true,
  onRefreshFrameRequest,
}: SetupEditorProps = {}) {
  const [zones, setZones] = useState<TableZone[]>(initialZones || []);
  const [selectedTableIndex, setSelectedTableIndex] = useState<number>(0);
  const [editingLayer, setEditingLayer] = useState<"display" | "counting">("display");
  const [frameUrl, setFrameUrl] = useState<string | null>(initialFrameUrl || null);
  const [frameDimensions, setFrameDimensions] = useState<{ width: number; height: number }>(
    initialFrameDimensions || { width: 1280, height: 720 }
  );
  const [dragTarget, setDragTarget] = useState<DragTarget | null>(null);
  const [loading, setLoading] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  // Video scrubber state
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentVideoTime, setCurrentVideoTime] = useState(0);
  const [videoDuration, setVideoDuration] = useState(0);

  const svgRef = useRef<SVGSVGElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    if (initialZones && initialZones.length > 0) {
      setZones(initialZones);
    }
    if (initialFrameUrl) {
      setFrameUrl(initialFrameUrl);
    }
    if (initialFrameDimensions) {
      setFrameDimensions(initialFrameDimensions);
    }
    if (!initialZones && !initialFrameUrl && !videoSource) {
      void loadInitial();
    }
  }, [initialZones, initialFrameUrl, initialFrameDimensions, videoSource]);

  async function loadInitial() {
    setLoading(true);
    setStatusMessage("Загрузка кадра и разметки столов...");
    try {
      const [frame, loadedZones] = await Promise.all([
        fetchCurrentFrame().catch(() => null),
        fetchTableZones().catch(() => []),
      ]);
      if (frame) {
        setFrameUrl(frame.url);
        setFrameDimensions({ width: frame.width, height: frame.height });
      }
      if (loadedZones && loadedZones.length > 0) {
        setZones(loadedZones);
      }
      setStatusMessage(null);
    } catch (err) {
      setStatusMessage("Ошибка при инициализации редактора: " + (err instanceof Error ? err.message : String(err)));
    } finally {
      setLoading(false);
    }
  }

  async function handleRefreshFrame() {
    setLoading(true);
    try {
      if (onRefreshFrameRequest) {
        const customRes = await onRefreshFrameRequest();
        if (customRes) {
          setFrameUrl(customRes.url);
          setFrameDimensions({ width: customRes.width, height: customRes.height });
        }
      } else {
        const frame = await fetchCurrentFrame();
        setFrameUrl(frame.url);
        setFrameDimensions({ width: frame.width, height: frame.height });
      }
      setStatusMessage("Кадр обновлён.");
    } catch (err) {
      setStatusMessage("Не удалось захватить кадр: " + (err instanceof Error ? err.message : String(err)));
    } finally {
      setLoading(false);
    }
  }

  function handleVideoLoadedMetadata() {
    if (videoRef.current) {
      const v = videoRef.current;
      setVideoDuration(v.duration || 0);
      if (v.videoWidth && v.videoHeight) {
        setFrameDimensions({ width: v.videoWidth, height: v.videoHeight });
      }
    }
  }

  function handleVideoTimeUpdate() {
    if (videoRef.current) {
      setCurrentVideoTime(videoRef.current.currentTime);
    }
  }

  function togglePlayPause() {
    if (!videoRef.current) return;
    if (videoRef.current.paused) {
      void videoRef.current.play();
      setIsPlaying(true);
    } else {
      videoRef.current.pause();
      setIsPlaying(false);
    }
  }

  function seekToTime(time: number) {
    if (!videoRef.current) return;
    videoRef.current.currentTime = time;
    setCurrentVideoTime(time);
  }

  function seekOffset(delta: number) {
    if (!videoRef.current) return;
    const dur = videoDuration || videoRef.current.duration || 100;
    const nextTime = Math.max(0, Math.min(dur, videoRef.current.currentTime + delta));
    seekToTime(nextTime);
  }

  async function handleAutoDetect() {
    setLoading(true);
    setStatusMessage("Выполняется автодетект столов нейросетью YOLO...");
    try {
      let detected: TableZone[] = [];
      if (videoSource && videoRef.current) {
        const v = videoRef.current;
        const canvas = document.createElement("canvas");
        const w = v.videoWidth || frameDimensions.width || 1280;
        const h = v.videoHeight || frameDimensions.height || 720;
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext("2d");
        if (ctx) {
          ctx.drawImage(v, 0, 0, w, h);
          const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.92));
          if (blob) {
            detected = await autoDetectTables(undefined, blob);
          }
        }
      } else if (frameUrl && frameUrl.startsWith("data:")) {
        // Convert data URL to blob
        const res = await fetch(frameUrl);
        const blob = await res.blob();
        detected = await autoDetectTables(undefined, blob);
      } else {
        detected = await autoDetectTables();
      }

      if (detected.length > 0) {
        setZones(detected);
        setSelectedTableIndex(0);
        setStatusMessage(`Автоматически найдено столов: ${detected.length}. Вы можете скорректировать вершины или добавить грани.`);
      } else {
        setStatusMessage("Столы не были обнаружены на этом кадре. Попробуйте промотать видео на другой момент.");
      }
    } catch (err) {
      setStatusMessage("Ошибка автодетекта: " + (err instanceof Error ? err.message : String(err)));
    } finally {
      setLoading(false);
    }
  }

  async function handleUploadVideo(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    setStatusMessage(`Загрузка видео «${file.name}»...`);
    try {
      await uploadVideoFile(file);
      setStatusMessage(`Видео успешно загружено. Получение первого кадра...`);
      // Wait a moment for processing to warm up
      setTimeout(async () => {
        await handleRefreshFrame();
        setUploading(false);
      }, 1000);
    } catch (err) {
      setStatusMessage("Ошибка загрузки видео: " + (err instanceof Error ? err.message : String(err)));
      setUploading(false);
    }
  }

  async function handleSave() {
    setLoading(true);
    setStatusMessage("Сохранение разметки столов...");
    try {
      if (onSaveZones) {
        onSaveZones(zones);
      }
      if (showSaveDbButton) {
        await saveTableZones(zones);
        setStatusMessage("✅ Разметка успешно сохранена в PostgreSQL и обновлена в AI-трансляции!");
      } else {
        setStatusMessage("✅ Разметка для видео успешно обновлена!");
      }
    } catch (err) {
      setStatusMessage("Ошибка сохранения: " + (err instanceof Error ? err.message : String(err)));
    } finally {
      setLoading(false);
    }
  }

  function handleAddTable() {
    const nextIdx = zones.length + 1;
    const name = `T-${nextIdx < 10 ? "0" + nextIdx : nextIdx}`;
    const cx = Math.round(frameDimensions.width / 2);
    const cy = Math.round(frameDimensions.height / 2);
    const w = 140;
    const h = 100;

    const displayPoly: PolygonPoint[] = [
      [cx - w, cy - h],
      [cx + w, cy - h],
      [cx + w, cy + h],
      [cx - w, cy + h],
    ];

    const countingPoly: PolygonPoint[] = [
      [cx - w - 40, cy - h - 40],
      [cx + w + 40, cy - h - 40],
      [cx + w + 40, cy + h + 40],
      [cx - w - 40, cy + h + 40],
    ];

    const newZone: TableZone = {
      id: name,
      name,
      capacity: 4,
      displayPolygon: displayPoly,
      countingPolygon: countingPoly,
      isActive: true,
    };

    setZones([...zones, newZone]);
    setSelectedTableIndex(zones.length);
    setStatusMessage(`Добавлен стол ${name}. Перетаскивайте точки или кликайте на ребра для скругления.`);
  }

  function handleDeleteSelectedTable() {
    if (zones.length === 0) return;
    const next = zones.filter((_, i) => i !== selectedTableIndex);
    setZones(next);
    setSelectedTableIndex(Math.max(0, selectedTableIndex - 1));
    setStatusMessage("Стол удалён.");
  }

  function getSvgCoordinates(e: ReactPointerEvent): { x: number; y: number } | null {
    if (!svgRef.current) return null;
    const pt = svgRef.current.createSVGPoint();
    pt.x = e.clientX;
    pt.y = e.clientY;
    const ctm = svgRef.current.getScreenCTM();
    if (!ctm) return null;
    const local = pt.matrixTransform(ctm.inverse());
    return {
      x: Math.round(Math.max(0, Math.min(frameDimensions.width, local.x))),
      y: Math.round(Math.max(0, Math.min(frameDimensions.height, local.y))),
    };
  }

  function handlePointerDownVertex(
    e: ReactPointerEvent,
    tableIndex: number,
    layer: "display" | "counting",
    pointIndex: number,
  ) {
    e.stopPropagation();
    e.preventDefault();
    (e.target as Element).setPointerCapture(e.pointerId);
    setSelectedTableIndex(tableIndex);
    setEditingLayer(layer);
    setDragTarget({ tableIndex, layer, pointIndex });
  }

  function handlePointerMove(e: ReactPointerEvent) {
    if (!dragTarget) return;
    const coords = getSvgCoordinates(e);
    if (!coords) return;

    setZones((prev) => {
      const next = [...prev];
      const table = { ...next[dragTarget.tableIndex] };
      const polyProp = dragTarget.layer === "display" ? "displayPolygon" : "countingPolygon";
      const poly = [...table[polyProp]];
      poly[dragTarget.pointIndex] = [coords.x, coords.y];
      table[polyProp] = poly;
      next[dragTarget.tableIndex] = table;
      return next;
    });
  }

  function handlePointerUp(e: ReactPointerEvent) {
    if (dragTarget) {
      try {
        (e.target as Element).releasePointerCapture(e.pointerId);
      } catch {
        // pointer capture already released
      }
      setDragTarget(null);
    }
  }

  // Insert vertex on edge click (splits the edge to create rounded/oval/custom tables!)
  function handleEdgeClick(
    e: ReactPointerEvent,
    tableIndex: number,
    layer: "display" | "counting",
    edgeStartIndex: number,
  ) {
    e.stopPropagation();
    const coords = getSvgCoordinates(e);
    if (!coords) return;

    setSelectedTableIndex(tableIndex);
    setEditingLayer(layer);

    setZones((prev) => {
      const next = [...prev];
      const table = { ...next[tableIndex] };
      const polyProp = layer === "display" ? "displayPolygon" : "countingPolygon";
      const poly = [...table[polyProp]];
      // Insert new point right after edgeStartIndex
      poly.splice(edgeStartIndex + 1, 0, [coords.x, coords.y]);
      table[polyProp] = poly;
      next[tableIndex] = table;
      return next;
    });

    setStatusMessage(`Добавлена новая вершина! Теперь у стола ${zones[tableIndex].name} ${zones[tableIndex].displayPolygon.length + 1} граней.`);
  }

  // Right click removes vertex
  function handleDeleteVertex(tableIndex: number, layer: "display" | "counting", pointIndex: number) {
    setZones((prev) => {
      const next = [...prev];
      const table = { ...next[tableIndex] };
      const polyProp = layer === "display" ? "displayPolygon" : "countingPolygon";
      const poly = [...table[polyProp]];
      if (poly.length <= 3) {
        setStatusMessage("Нельзя удалить: у стола должно быть как минимум 3 вершины.");
        return prev;
      }
      poly.splice(pointIndex, 1);
      table[polyProp] = poly;
      next[tableIndex] = table;
      return next;
    });
    setStatusMessage("Вершина удалена.");
  }

  const selectedTable = zones[selectedTableIndex] || null;

  return (
    <div className="polygon-editor-container">
      <div className="editor-topbar">
        <div className="editor-title-group">
          <h2>{title}</h2>
          <p className="editor-subtitle">{subtitle}</p>
        </div>

        <div className="editor-actions">
          {showVideoUpload && (
            <label className="secondary-button upload-video-label">
              📁 Загрузить видео
              <input type="file" accept="video/*" style={{ display: "none" }} onChange={handleUploadVideo} disabled={uploading} />
            </label>
          )}
          <button type="button" className="secondary-button" onClick={handleRefreshFrame} disabled={loading}>
            📸 Обновить кадр
          </button>
          <button type="button" className="secondary-button primary-accent" onClick={handleAutoDetect} disabled={loading}>
            🤖 Автодетект столов (YOLO)
          </button>
          <button type="button" className="secondary-button" onClick={handleAddTable}>
            ➕ Добавить стол
          </button>
          <button type="button" className="primary-button save-btn" onClick={handleSave} disabled={loading}>
            {saveButtonLabel}
          </button>
        </div>
      </div>

      {statusMessage && (
        <div className={`editor-alert ${statusMessage.startsWith("✅") ? "success" : "info"}`}>
          {statusMessage}
        </div>
      )}

      <div className="editor-main-layout">
        <div className="editor-viewport">
          <div className="layer-mode-selector">
            <span className="layer-label">Режим редактирования:</span>
            <button
              type="button"
              className={`layer-btn display-layer ${editingLayer === "display" ? "active" : ""}`}
              onClick={() => setEditingLayer("display")}
            >
              🟦 Столешница (Контур стола)
            </button>
            <button
              type="button"
              className={`layer-btn counting-layer ${editingLayer === "counting" ? "active" : ""}`}
              onClick={() => setEditingLayer("counting")}
            >
              🟨 Зона гостей (Стулья / Посадка)
            </button>
          </div>

          <div
            className="canvas-stage"
            style={{ aspectRatio: `${frameDimensions.width} / ${frameDimensions.height}` }}
            onPointerMove={handlePointerMove}
            onPointerUp={handlePointerUp}
          >
            {videoSource ? (
              <video
                ref={videoRef}
                src={videoSource}
                playsInline
                muted
                preload="auto"
                onLoadedMetadata={handleVideoLoadedMetadata}
                onTimeUpdate={handleVideoTimeUpdate}
                className="stage-background-img"
              />
            ) : frameUrl ? (
              <img src={frameUrl} alt="Захваченный стоп-кадр" className="stage-background-img" />
            ) : (
              <div className="stage-no-frame">Загрузка кадра видеопотока...</div>
            )}

            <svg
              ref={svgRef}
              viewBox={`0 0 ${frameDimensions.width} ${frameDimensions.height}`}
              className="stage-svg-overlay"
            >
              {zones.map((table, tIdx) => {
                const isSelected = tIdx === selectedTableIndex;
                const activePoly = editingLayer === "display" ? table.displayPolygon : table.countingPolygon;
                const inactivePoly = editingLayer === "display" ? table.countingPolygon : table.displayPolygon;

                const displayPointsStr = table.displayPolygon.map((p) => `${p[0]},${p[1]}`).join(" ");
                const countingPointsStr = table.countingPolygon.map((p) => `${p[0]},${p[1]}`).join(" ");

                // Center for table badge
                const xs = table.displayPolygon.map((p) => p[0]);
                const ys = table.displayPolygon.map((p) => p[1]);
                const minX = xs.length ? Math.min(...xs) : 0;
                const maxX = xs.length ? Math.max(...xs) : 0;
                const minY = ys.length ? Math.min(...ys) : 0;
                const maxY = ys.length ? Math.max(...ys) : 0;
                const centerX = (minX + maxX) / 2;
                const centerY = (minY + maxY) / 2;

                return (
                  <g key={table.id || tIdx} className={`table-group ${isSelected ? "selected-group" : ""}`}>
                    {/* Counting Polygon (Seating zone) */}
                    <polygon
                      points={countingPointsStr}
                      className={`counting-poly-shape ${isSelected && editingLayer === "counting" ? "active-poly" : ""}`}
                      onClick={() => {
                        setSelectedTableIndex(tIdx);
                        setEditingLayer("counting");
                      }}
                    />

                    {/* Display Polygon (Tabletop) */}
                    <polygon
                      points={displayPointsStr}
                      className={`display-poly-shape ${isSelected && editingLayer === "display" ? "active-poly" : ""}`}
                      onClick={() => {
                        setSelectedTableIndex(tIdx);
                        setEditingLayer("display");
                      }}
                    />

                    {/* Edge interaction zones (Click edge to insert vertex) */}
                    {isSelected &&
                      activePoly.map((p1, pIdx) => {
                        const p2 = activePoly[(pIdx + 1) % activePoly.length];
                        const midX = (p1[0] + p2[0]) / 2;
                        const midY = (p1[1] + p2[1]) / 2;

                        return (
                          <g key={`edge-${pIdx}`} className="edge-group">
                            <line
                              x1={p1[0]}
                              y1={p1[1]}
                              x2={p2[0]}
                              y2={p2[1]}
                              className="edge-hover-line"
                              onPointerDown={(e) => handleEdgeClick(e, tIdx, editingLayer, pIdx)}
                            />
                            {/* Midpoint '+' handle */}
                            <circle
                              cx={midX}
                              cy={midY}
                              r={5}
                              className="edge-split-handle"
                              onPointerDown={(e) => handleEdgeClick(e, tIdx, editingLayer, pIdx)}
                            />
                          </g>
                        );
                      })}

                    {/* Vertex Draggable Handles */}
                    {isSelected &&
                      activePoly.map((pt, pIdx) => (
                        <circle
                          key={`vertex-${pIdx}`}
                          cx={pt[0]}
                          cy={pt[1]}
                          r={7}
                          className="poly-vertex-handle"
                          onPointerDown={(e) => handlePointerDownVertex(e, tIdx, editingLayer, pIdx)}
                          onContextMenu={(e) => {
                            e.preventDefault();
                            handleDeleteVertex(tIdx, editingLayer, pIdx);
                          }}
                        />
                      ))}

                    {/* Table Name Badge */}
                    <g
                      transform={`translate(${centerX}, ${centerY})`}
                      className="table-badge-group"
                      onClick={() => setSelectedTableIndex(tIdx)}
                    >
                      <rect x="-35" y="-14" width="70" height="24" rx="4" className="badge-bg" />
                      <text x="0" y="3" textAnchor="middle" className="badge-text">
                        {table.name} ({table.capacity})
                      </text>
                    </g>
                  </g>
                );
              })}
            </svg>
          </div>

          {/* Video Scrubbing & Playback Bar */}
          {videoSource && (
            <div className="editor-playback-bar">
              <div className="playback-controls-row">
                <button
                  type="button"
                  className="secondary-button play-toggle-btn"
                  onClick={togglePlayPause}
                >
                  {isPlaying ? "⏸️ Пауза" : "▶️ Воспроизведение"}
                </button>

                <div className="playback-step-buttons">
                  <button
                    type="button"
                    className="step-btn"
                    onClick={() => seekOffset(-1.0)}
                    title="Назад на 1 секунду"
                  >
                    ⏪ -1с
                  </button>
                  <button
                    type="button"
                    className="step-btn"
                    onClick={() => seekOffset(-0.04)}
                    title="Назад на 1 кадр"
                  >
                    ◀
                  </button>
                  <button
                    type="button"
                    className="step-btn"
                    onClick={() => seekOffset(0.04)}
                    title="Вперед на 1 кадр"
                  >
                    ▶
                  </button>
                  <button
                    type="button"
                    className="step-btn"
                    onClick={() => seekOffset(1.0)}
                    title="Вперед на 1 секунду"
                  >
                    +1с ⏩
                  </button>
                </div>

                <div className="scrubber-range-container">
                  <input
                    type="range"
                    min={0}
                    max={videoDuration || 1}
                    step={0.04}
                    value={currentVideoTime}
                    onChange={(e) => seekToTime(parseFloat(e.target.value))}
                    className="timeline-scrubber"
                  />
                </div>

                <span className="scrubber-timecode">
                  ⏱️ {formatTime(currentVideoTime)} / {formatTime(videoDuration)}
                </span>
              </div>
            </div>
          )}
        </div>

        {/* Sidebar Properties Panel */}
        <aside className="editor-sidebar">
          <h3>Параметры столов</h3>
          <div className="tables-list-chips">
            {zones.map((z, idx) => (
              <button
                type="button"
                key={z.id || idx}
                className={`table-chip ${idx === selectedTableIndex ? "active" : ""}`}
                onClick={() => setSelectedTableIndex(idx)}
              >
                {z.name} ({z.capacity}м)
              </button>
            ))}
          </div>

          {selectedTable ? (
            <div className="selected-table-card">
              <h4>Стол: {selectedTable.name}</h4>
              <div className="prop-row">
                <label>
                  Имя стола:
                  <input
                    type="text"
                    value={selectedTable.name}
                    onChange={(e) => {
                      const val = e.target.value;
                      setZones((prev) => {
                        const next = [...prev];
                        next[selectedTableIndex] = { ...next[selectedTableIndex], name: val };
                        return next;
                      });
                    }}
                  />
                </label>
              </div>

              <div className="prop-row">
                <label>
                  Вместимость (гостей):
                  <input
                    type="number"
                    min="1"
                    max="20"
                    value={selectedTable.capacity}
                    onChange={(e) => {
                      const val = parseInt(e.target.value, 10) || 1;
                      setZones((prev) => {
                        const next = [...prev];
                        next[selectedTableIndex] = { ...next[selectedTableIndex], capacity: val };
                        return next;
                      });
                    }}
                  />
                </label>
              </div>

              <div className="vertex-count-info">
                <span>
                  Точек столешницы: <strong>{selectedTable.displayPolygon.length}</strong>
                </span>
                <span>
                  Точек зоны гостей: <strong>{selectedTable.countingPolygon.length}</strong>
                </span>
              </div>

              <div className="round-table-tip">
                💡 <strong>Подсказка для круглых столов:</strong>
                <p>
                  Кликайте на любое ребро стола (подсвечивается фиолетовым с плюсиком), чтобы добавить точки и получить круглую или овальную форму!
                </p>
              </div>

              <div className="table-actions-group">
                <button
                  type="button"
                  className="secondary-button danger-btn"
                  onClick={handleDeleteSelectedTable}
                >
                  🗑️ Удалить этот стол
                </button>
              </div>
            </div>
          ) : (
            <p className="no-selection-hint">Выберите стол на кадре для настройки.</p>
          )}
        </aside>
      </div>
    </div>
  );
}
