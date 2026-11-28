import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { PolygonPoint, TableZone } from "../types";
import { autoDetectTables, fetchCurrentFrame, fetchTableZones, saveTableZones, uploadVideoFile } from "../api";

interface DragStateVertex {
  kind: "vertex";
  tableIndex: number;
  layer: "display" | "counting";
  pointIndex: number;
}

interface DragStateTable {
  kind: "whole_table";
  tableIndex: number;
  syncZones: boolean;
  startSvg: { x: number; y: number };
  initialDisplay: PolygonPoint[];
  initialCounting: PolygonPoint[];
}

type DragState = DragStateVertex | DragStateTable | null;

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
  const [syncBothZones, setSyncBothZones] = useState<boolean>(true);
  const [selectedVertexIndex, setSelectedVertexIndex] = useState<number | null>(null);
  const [isDragging, setIsDragging] = useState<boolean>(false);
  const dragTargetRef = useRef<DragState>(null);
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

  function getSvgPoint(
    svg: SVGSVGElement | null,
    clientX: number,
    clientY: number,
    frameWidth: number,
    frameHeight: number
  ): { x: number; y: number } | null {
    if (!svg) return null;
    const pt = svg.createSVGPoint();
    pt.x = clientX;
    pt.y = clientY;
    const ctm = svg.getScreenCTM();
    if (!ctm) return null;
    const local = pt.matrixTransform(ctm.inverse());
    return {
      x: Math.round(Math.max(0, Math.min(frameWidth, local.x))),
      y: Math.round(Math.max(0, Math.min(frameHeight, local.y))),
    };
  }

  function startDrag(drag: NonNullable<DragState>) {
    dragTargetRef.current = drag;
    setIsDragging(true);

    const handleWindowPointerMove = (ev: PointerEvent) => {
      const cur = dragTargetRef.current;
      if (!cur || !svgRef.current) return;

      const coords = getSvgPoint(
        svgRef.current,
        ev.clientX,
        ev.clientY,
        frameDimensions.width,
        frameDimensions.height
      );
      if (!coords) return;

      if (cur.kind === "vertex") {
        setZones((prev) => {
          if (!prev[cur.tableIndex]) return prev;
          const next = [...prev];
          const table = { ...next[cur.tableIndex] };
          const polyProp = cur.layer === "display" ? "displayPolygon" : "countingPolygon";
          const poly = table[polyProp].map((p, idx) =>
            idx === cur.pointIndex ? ([coords.x, coords.y] as PolygonPoint) : ([...p] as PolygonPoint)
          );
          table[polyProp] = poly;
          next[cur.tableIndex] = table;
          return next;
        });
      } else if (cur.kind === "whole_table") {
        const dx = coords.x - cur.startSvg.x;
        const dy = coords.y - cur.startSvg.y;

        const allPoints = cur.syncZones
          ? [...cur.initialDisplay, ...cur.initialCounting]
          : editingLayer === "display"
          ? cur.initialDisplay
          : cur.initialCounting;

        const minX = Math.min(...allPoints.map((p) => p[0]));
        const maxX = Math.max(...allPoints.map((p) => p[0]));
        const minY = Math.min(...allPoints.map((p) => p[1]));
        const maxY = Math.max(...allPoints.map((p) => p[1]));

        const clampedDx = Math.max(-minX, Math.min(frameDimensions.width - maxX, dx));
        const clampedDy = Math.max(-minY, Math.min(frameDimensions.height - maxY, dy));

        setZones((prev) => {
          if (!prev[cur.tableIndex]) return prev;
          const next = [...prev];
          const table = { ...next[cur.tableIndex] };

          if (cur.syncZones) {
            table.displayPolygon = cur.initialDisplay.map(([x, y]) => [
              Math.round(x + clampedDx),
              Math.round(y + clampedDy),
            ]);
            table.countingPolygon = cur.initialCounting.map(([x, y]) => [
              Math.round(x + clampedDx),
              Math.round(y + clampedDy),
            ]);
          } else if (editingLayer === "display") {
            table.displayPolygon = cur.initialDisplay.map(([x, y]) => [
              Math.round(x + clampedDx),
              Math.round(y + clampedDy),
            ]);
          } else {
            table.countingPolygon = cur.initialCounting.map(([x, y]) => [
              Math.round(x + clampedDx),
              Math.round(y + clampedDy),
            ]);
          }

          next[cur.tableIndex] = table;
          return next;
        });
      }
    };

    const handleWindowPointerUp = () => {
      dragTargetRef.current = null;
      setIsDragging(false);
      window.removeEventListener("pointermove", handleWindowPointerMove);
      window.removeEventListener("pointerup", handleWindowPointerUp);
      window.removeEventListener("pointercancel", handleWindowPointerUp);
    };

    window.addEventListener("pointermove", handleWindowPointerMove, { passive: false });
    window.addEventListener("pointerup", handleWindowPointerUp);
    window.addEventListener("pointercancel", handleWindowPointerUp);
  }

  function handlePolygonPointerDown(
    e: ReactPointerEvent,
    tableIndex: number,
    layer: "display" | "counting"
  ) {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();

    setSelectedTableIndex(tableIndex);
    setEditingLayer(layer);
    setSelectedVertexIndex(null);

    const coords = getSvgPoint(
      svgRef.current,
      e.clientX,
      e.clientY,
      frameDimensions.width,
      frameDimensions.height
    );
    if (!coords) return;

    const currentTable = zones[tableIndex];
    if (!currentTable) return;

    startDrag({
      kind: "whole_table",
      tableIndex,
      syncZones: syncBothZones,
      startSvg: coords,
      initialDisplay: currentTable.displayPolygon.map((p) => [...p]),
      initialCounting: currentTable.countingPolygon.map((p) => [...p]),
    });
  }

  function handleVertexPointerDown(
    e: ReactPointerEvent,
    tableIndex: number,
    layer: "display" | "counting",
    pointIndex: number
  ) {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();

    setSelectedTableIndex(tableIndex);
    setEditingLayer(layer);
    setSelectedVertexIndex(pointIndex);

    startDrag({
      kind: "vertex",
      tableIndex,
      layer,
      pointIndex,
    });
  }

  function handleInsertVertexAtEdge(
    tableIndex: number,
    layer: "display" | "counting",
    edgeStartIndex: number,
    insertCoords?: { x: number; y: number },
    startDraggingImmediately: boolean = false
  ) {
    const newIdx = edgeStartIndex + 1;
    setSelectedTableIndex(tableIndex);
    setEditingLayer(layer);
    setSelectedVertexIndex(newIdx);

    setZones((prev) => {
      if (!prev[tableIndex]) return prev;
      const next = [...prev];
      const table = { ...next[tableIndex] };
      const polyProp = layer === "display" ? "displayPolygon" : "countingPolygon";
      const poly = [...table[polyProp]];

      let pt: PolygonPoint;
      if (insertCoords) {
        pt = [insertCoords.x, insertCoords.y];
      } else {
        const p1 = poly[edgeStartIndex];
        const p2 = poly[(edgeStartIndex + 1) % poly.length];
        pt = [Math.round((p1[0] + p2[0]) / 2), Math.round((p1[1] + p2[1]) / 2)];
      }

      poly.splice(newIdx, 0, pt);
      table[polyProp] = poly;
      next[tableIndex] = table;
      return next;
    });

    setStatusMessage(`✨ Добавлена новая вершина! Перетаскивайте её для скругления стола.`);

    if (startDraggingImmediately) {
      startDrag({
        kind: "vertex",
        tableIndex,
        layer,
        pointIndex: newIdx,
      });
    }
  }

  function handleDeleteVertex(tableIndex: number, layer: "display" | "counting", pointIndex: number) {
    setZones((prev) => {
      if (!prev[tableIndex]) return prev;
      const next = [...prev];
      const table = { ...next[tableIndex] };
      const polyProp = layer === "display" ? "displayPolygon" : "countingPolygon";
      const poly = [...table[polyProp]];
      if (poly.length <= 3) {
        setStatusMessage("⚠️ Нельзя удалить: у стола должно оставаться не менее 3 вершин.");
        return prev;
      }
      poly.splice(pointIndex, 1);
      table[polyProp] = poly;
      next[tableIndex] = table;
      return next;
    });
    setSelectedVertexIndex(null);
    setStatusMessage("🗑️ Вершина удалена.");
  }

  function handleAddVertexToLongestEdge() {
    const table = zones[selectedTableIndex];
    if (!table) return;
    const poly = editingLayer === "display" ? table.displayPolygon : table.countingPolygon;
    let maxLen = -1;
    let longestEdgeIdx = 0;
    for (let i = 0; i < poly.length; i++) {
      const p1 = poly[i];
      const p2 = poly[(i + 1) % poly.length];
      const len = Math.hypot(p2[0] - p1[0], p2[1] - p1[1]);
      if (len > maxLen) {
        maxLen = len;
        longestEdgeIdx = i;
      }
    }
    handleInsertVertexAtEdge(selectedTableIndex, editingLayer, longestEdgeIdx);
  }

  function handleMakeRound(tableIndex: number, pointCount: number = 8) {
    setZones((prev) => {
      if (!prev[tableIndex]) return prev;
      const next = [...prev];
      const table = { ...next[tableIndex] };

      const xs = table.displayPolygon.map((p) => p[0]);
      const ys = table.displayPolygon.map((p) => p[1]);
      const minX = Math.min(...xs);
      const maxX = Math.max(...xs);
      const minY = Math.min(...ys);
      const maxY = Math.max(...ys);

      const cx = (minX + maxX) / 2;
      const cy = (minY + maxY) / 2;
      const rx = Math.max(20, (maxX - minX) / 2);
      const ry = Math.max(20, (maxY - minY) / 2);
      const pad = 35;

      const newDisplay: PolygonPoint[] = [];
      const newCounting: PolygonPoint[] = [];

      for (let i = 0; i < pointCount; i++) {
        const angle = (2 * Math.PI * i) / pointCount - Math.PI / 2;
        const cosA = Math.cos(angle);
        const sinA = Math.sin(angle);
        newDisplay.push([
          Math.round(cx + rx * cosA),
          Math.round(cy + ry * sinA),
        ]);
        newCounting.push([
          Math.round(cx + (rx + pad) * cosA),
          Math.round(cy + (ry + pad) * sinA),
        ]);
      }

      table.displayPolygon = newDisplay;
      table.countingPolygon = newCounting;
      next[tableIndex] = table;
      return next;
    });
    setSelectedVertexIndex(null);
    setStatusMessage(`⚪ Стол преобразован в круглый/овальный (${pointCount} вершин). Перетаскивайте точки для подгонки.`);
  }

  function handleMakeRectangle(tableIndex: number) {
    setZones((prev) => {
      if (!prev[tableIndex]) return prev;
      const next = [...prev];
      const table = { ...next[tableIndex] };

      const xs = table.displayPolygon.map((p) => p[0]);
      const ys = table.displayPolygon.map((p) => p[1]);
      const minX = Math.min(...xs);
      const maxX = Math.max(...xs);
      const minY = Math.min(...ys);
      const maxY = Math.max(...ys);
      const pad = 35;

      table.displayPolygon = [
        [minX, minY],
        [maxX, minY],
        [maxX, maxY],
        [minX, maxY],
      ];
      table.countingPolygon = [
        [minX - pad, minY - pad],
        [maxX + pad, minY - pad],
        [maxX + pad, maxY + pad],
        [minX - pad, maxY + pad],
      ];
      next[tableIndex] = table;
      return next;
    });
    setSelectedVertexIndex(null);
    setStatusMessage("⬛ Стол преобразован в прямоугольный (4 вершины).");
  }

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (
        document.activeElement?.tagName === "INPUT" ||
        document.activeElement?.tagName === "TEXTAREA"
      ) {
        return;
      }

      if (e.key === "Delete" || e.key === "Backspace") {
        if (selectedVertexIndex !== null && zones[selectedTableIndex]) {
          e.preventDefault();
          handleDeleteVertex(selectedTableIndex, editingLayer, selectedVertexIndex);
        }
      } else if (e.key === "Escape") {
        setSelectedVertexIndex(null);
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedVertexIndex, selectedTableIndex, editingLayer, zones]);

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
            className={`canvas-stage ${isDragging ? "dragging" : ""}`}
            style={{ aspectRatio: `${frameDimensions.width} / ${frameDimensions.height}` }}
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
                    {/* 1. Counting Polygon (Seating zone / Chairs) */}
                    <polygon
                      points={countingPointsStr}
                      className={`counting-poly-shape ${isSelected && editingLayer === "counting" ? "active-poly" : ""}`}
                      onPointerDown={(e) => handlePolygonPointerDown(e, tIdx, "counting")}
                    />

                    {/* 2. Display Polygon (Tabletop) */}
                    <polygon
                      points={displayPointsStr}
                      className={`display-poly-shape ${isSelected && editingLayer === "display" ? "active-poly" : ""}`}
                      onPointerDown={(e) => handlePolygonPointerDown(e, tIdx, "display")}
                    />

                    {/* 3. Edge interaction zones (Click edge or '+' to insert vertex) */}
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
                              onPointerDown={(e) => {
                                if (e.button !== 0) return;
                                e.stopPropagation();
                                e.preventDefault();
                                const coords = getSvgPoint(svgRef.current, e.clientX, e.clientY, frameDimensions.width, frameDimensions.height);
                                handleInsertVertexAtEdge(tIdx, editingLayer, pIdx, coords || undefined, true);
                              }}
                            />
                            {/* Midpoint '+' handle */}
                            <g
                              className="edge-split-handle-group"
                              onPointerDown={(e) => {
                                if (e.button !== 0) return;
                                e.stopPropagation();
                                e.preventDefault();
                                handleInsertVertexAtEdge(tIdx, editingLayer, pIdx, { x: midX, y: midY }, true);
                              }}
                            >
                              <circle cx={midX} cy={midY} r={12} className="edge-split-hit-area" />
                              <circle cx={midX} cy={midY} r={6} className="edge-split-handle" />
                              <text x={midX} y={midY + 3.5} textAnchor="middle" className="edge-split-plus">
                                +
                              </text>
                            </g>
                          </g>
                        );
                      })}

                    {/* 4. Vertex Draggable Handles (Rendered on top of edges for perfect hit testing) */}
                    {isSelected &&
                      activePoly.map((pt, pIdx) => {
                        const isVertexSelected = selectedVertexIndex === pIdx;
                        return (
                          <g
                            key={`vertex-${pIdx}`}
                            className={`poly-vertex-group ${isVertexSelected ? "vertex-active" : ""}`}
                            onPointerDown={(e) => handleVertexPointerDown(e, tIdx, editingLayer, pIdx)}
                            onContextMenu={(e) => {
                              e.preventDefault();
                              e.stopPropagation();
                              handleDeleteVertex(tIdx, editingLayer, pIdx);
                            }}
                            onDoubleClick={(e) => {
                              e.stopPropagation();
                              handleDeleteVertex(tIdx, editingLayer, pIdx);
                            }}
                          >
                            {/* Large invisible hit area for effortless clicking and dragging */}
                            <circle cx={pt[0]} cy={pt[1]} r={14} className="poly-vertex-hit-area" />
                            {/* Visible vertex circle */}
                            <circle
                              cx={pt[0]}
                              cy={pt[1]}
                              r={isVertexSelected ? 8 : 6.5}
                              className={`poly-vertex-handle ${isVertexSelected ? "selected" : ""}`}
                            />
                            {/* Point index badge when selected */}
                            {isVertexSelected && (
                              <text x={pt[0]} y={pt[1] - 12} textAnchor="middle" className="vertex-index-label">
                                #{pIdx + 1}
                              </text>
                            )}
                          </g>
                        );
                      })}

                    {/* 5. Table Name Badge (Also acts as a drag handle for the whole table) */}
                    <g
                      transform={`translate(${centerX}, ${centerY})`}
                      className="table-badge-group"
                      onPointerDown={(e) => handlePolygonPointerDown(e, tIdx, "display")}
                    >
                      <rect x="-38" y="-14" width="76" height="25" rx="5" className="badge-bg" />
                      <text x="0" y="3.5" textAnchor="middle" className="badge-text">
                        {table.name} ({table.capacity}м)
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

              {/* Mode: Synchronous movement of both zones */}
              <div className="sync-zones-toggle">
                <label className="checkbox-control">
                  <input
                    type="checkbox"
                    checked={syncBothZones}
                    onChange={(e) => setSyncBothZones(e.target.checked)}
                  />
                  <span>🔗 Двигать стол и стулья вместе</span>
                </label>
              </div>

              {/* Geometry Tools & Shape Presets */}
              <div className="geometry-tools-section">
                <div className="section-label">Форма и вершины стола:</div>
                <div className="geometry-buttons-row">
                  <button
                    type="button"
                    className="secondary-button geom-btn"
                    onClick={handleAddVertexToLongestEdge}
                    title="Добавить вершину на самое длинное ребро"
                  >
                    ➕ Добавить грань
                  </button>
                  {selectedVertexIndex !== null && (
                    <button
                      type="button"
                      className="secondary-button danger-btn geom-btn"
                      onClick={() => handleDeleteVertex(selectedTableIndex, editingLayer, selectedVertexIndex)}
                      title="Удалить выбранную точку (клавиша Delete)"
                    >
                      🗑️ Удалить точку #{selectedVertexIndex + 1}
                    </button>
                  )}
                </div>

                <div className="shape-presets-container">
                  <span className="preset-hint-label">Быстрые шаблоны формы:</span>
                  <div className="preset-buttons-group">
                    <button
                      type="button"
                      className="preset-pill-btn"
                      onClick={() => handleMakeRound(selectedTableIndex, 8)}
                      title="Сделать круглым с 8 вершинами"
                    >
                      ⚪ Круг (8)
                    </button>
                    <button
                      type="button"
                      className="preset-pill-btn"
                      onClick={() => handleMakeRound(selectedTableIndex, 12)}
                      title="Сделать овальным с 12 вершинами"
                    >
                      🔵 Овал (12)
                    </button>
                    <button
                      type="button"
                      className="preset-pill-btn"
                      onClick={() => handleMakeRectangle(selectedTableIndex)}
                      title="Сбросить в прямоугольник"
                    >
                      ⬛ Прямоугольник
                    </button>
                  </div>
                </div>
              </div>

              <div className="round-table-tip">
                💡 <strong>Управление разметкой:</strong>
                <p>
                  • <strong>Перемещение стола:</strong> зажмите и тащите внутри стола (или за бейдж).
                </p>
                <p>
                  • <strong>Форма стола:</strong> тащите белые точки вершин.
                </p>
                <p>
                  • <strong>Добавить точку:</strong> кликайте на фиолетовый <strong>+</strong> на ребре.
                </p>
                <p>
                  • <strong>Удалить точку:</strong> правый или двойной клик на точку (или кнопка «Удалить»).
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
