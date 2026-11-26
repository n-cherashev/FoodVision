import { useRef, useState, type PointerEvent } from "react";
import type { Bounds, TableConfiguration } from "../types";

interface SetupEditorProps {
  source: string;
  tables: TableConfiguration[];
  onChange(tables: TableConfiguration[]): void;
  onNext?(): void;
}
type Shape = "bounds" | `zone-${number}`;
type EditorMode = { kind: "idle" } | { kind: "add-table" } | { kind: "add-zone"; tableIndex: number };
type DragState = { tableIndex: number; shape: Shape; start: Point } | null;
interface Point { x: number; y: number; }

export function SetupEditor({ source, tables, onChange, onNext }: SetupEditorProps) {
  const canvasRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [duration, setDuration] = useState(0);
  const [time, setTime] = useState(0);
  const [mode, setMode] = useState<EditorMode>({ kind: "idle" });
  const [drag, setDrag] = useState<DragState>(null);

  function changeTable(index: number, update: (table: TableConfiguration) => TableConfiguration) {
    onChange(tables.map((table, current) => current === index ? update(table) : table));
  }

  function editShape(tableIndex: number, shape: Shape, update: (bounds: Bounds) => Bounds) {
    changeTable(tableIndex, (table) => {
      if (shape === "bounds") return { ...table, bounds: normalize(update(table.bounds)) };
      const seatingZones = [...table.seatingZones];
      const zoneIndex = Number(shape.slice(5));
      seatingZones[zoneIndex] = normalize(update(seatingZones[zoneIndex]));
      return { ...table, seatingZones };
    });
  }

  function editCoordinate(tableIndex: number, shape: Shape, key: keyof Bounds, value: number) {
    editShape(tableIndex, shape, bounds => ({ ...bounds, [key]: value }));
  }

  function point(event: PointerEvent<HTMLDivElement>): Point | null {
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect || rect.width === 0 || rect.height === 0) return null;
    return { x: clamp((event.clientX - rect.left) / rect.width, 0, 1), y: clamp((event.clientY - rect.top) / rect.height, 0, 1) };
  }

  function beginDrag(event: PointerEvent<HTMLDivElement>, tableIndex: number, shape: Shape, bounds: Bounds) {
    if (mode.kind !== "idle") return;
    const start = point(event);
    if (!start) return;
    event.stopPropagation();
    event.currentTarget.setPointerCapture(event.pointerId);
    setMode({ kind: "idle" });
    setDrag({ tableIndex, shape, start });
  }

  function moveShape(event: PointerEvent<HTMLDivElement>) {
    const current = point(event);
    if (!current || !drag) return;
    const deltaX = current.x - drag.start.x;
    const deltaY = current.y - drag.start.y;
    editShape(drag.tableIndex, drag.shape, bounds => ({ ...bounds, x: bounds.x + deltaX, y: bounds.y + deltaY }));
    setDrag({ ...drag, start: current });
  }

  function beginDrawing(event: PointerEvent<HTMLDivElement>) {
    const start = point(event);
    if (!start || mode.kind === "idle") return;
    event.currentTarget.setPointerCapture(event.pointerId);
    if (mode.kind === "add-table") {
      const tableIndex = tables.length;
      onChange([...tables, { id: `table-${Date.now()}`, bounds: { x: start.x, y: start.y, width: .01, height: .01 }, seatingZones: [{ x: start.x, y: start.y, width: .01, height: .01 }] }]);
      setDrag({ tableIndex, shape: "bounds", start });
      return;
    }
    const tableIndex = mode.tableIndex;
    const zoneIndex = tables[tableIndex].seatingZones.length;
    changeTable(tableIndex, table => ({ ...table, seatingZones: [...table.seatingZones, { x: start.x, y: start.y, width: .01, height: .01 }] }));
    setDrag({ tableIndex, shape: `zone-${zoneIndex}`, start });
  }

  function resizeCreated(event: PointerEvent<HTMLDivElement>) {
    const current = point(event);
    if (!current || !drag || mode.kind === "idle") return;
    const bounds = rectangleFrom(drag.start, current);
    if (mode.kind === "add-table") {
      changeTable(drag.tableIndex, table => ({ ...table, bounds, seatingZones: [bounds, ...table.seatingZones.slice(1)] }));
      return;
    }
    editShape(drag.tableIndex, drag.shape, () => bounds);
  }

  function finishInteraction() { setDrag(null); if (mode.kind !== "idle") setMode({ kind: "idle" }); }

  return (
    <section className="setup-editor" aria-labelledby="setup-heading">
      <div className="setup-heading">
        <div><p className="eyebrow">Шаг 2 · Проверка разметки</p><h2 id="setup-heading">Проверьте столы и зоны посадки</h2></div>
        <button type="button" className={mode.kind === "add-table" ? "secondary-button active-tool" : "secondary-button"} onClick={() => setMode(mode.kind === "add-table" ? { kind: "idle" } : { kind: "add-table" })}>+ Нарисовать стол</button>
      </div>
      <p className="editor-help">Нажмите «Нарисовать стол», затем протяните рамку на кадре. Существующие рамки можно перетаскивать. Для каждого стола нарисуйте одну или несколько зон посадки; числовые поля остаются доступны для точной правки.</p>
      <div className="video-frame editor-video">
        <video ref={videoRef} src={source} preload="metadata" onLoadedMetadata={event => setDuration(event.currentTarget.duration)} onTimeUpdate={event => setTime(event.currentTarget.currentTime)} />
        <div ref={canvasRef} className={`table-overlay layout-canvas ${mode.kind !== "idle" ? "drawing" : ""}`} onPointerDown={beginDrawing} onPointerMove={event => { if (mode.kind === "idle") moveShape(event); else resizeCreated(event); }} onPointerUp={finishInteraction} onPointerCancel={finishInteraction}>
          {tables.map((table, index) => <div key={table.id}>
            <div className="table-marker editable-marker" style={style(table.bounds)} onPointerDown={event => beginDrag(event, index, "bounds", table.bounds)} role="button" tabIndex={0} aria-label={`Переместить стол ${index + 1}`}><span>Стол {index + 1}</span></div>
            {table.seatingZones.map((zone, zoneIndex) => <div className="zone-marker editable-marker" key={zoneIndex} style={style(zone)} onPointerDown={event => beginDrag(event, index, `zone-${zoneIndex}`, zone)} role="button" tabIndex={0} aria-label={`Переместить зону посадки ${zoneIndex + 1} стола ${index + 1}`}><span>Место {zoneIndex + 1}</span></div>)}
          </div>)}
        </div>
      </div>
      <div className="editor-playback"><button type="button" className="secondary-button" onClick={() => { const video = videoRef.current; if (video) { if (video.paused) void video.play(); else video.pause(); } }}>Воспроизвести / пауза</button><label>Кадр <input type="range" min="0" max={duration || 0} step="0.1" value={Math.min(time, duration || 0)} onChange={event => { if (videoRef.current) videoRef.current.currentTime = Number(event.target.value); }} /></label><span>{Math.floor(time)} с</span></div>
      {mode.kind !== "idle" && <p className="drawing-hint" role="status">Протяните указатель по кадру, чтобы задать {mode.kind === "add-table" ? "границы стола" : "зону посадки"}.</p>}
      {tables.length === 0 && <p className="message empty">Столы автоматически не найдены. Нажмите «Нарисовать стол» и добавьте их на кадр.</p>}
      <div className="table-configurations">
        {tables.map((table, index) => <article className="table-config" id={`table-config-${index + 1}`} key={table.id}>
          <div className="table-config-heading"><h3>Стол {index + 1}</h3><button type="button" className="text-button danger-text" onClick={() => onChange(tables.filter((_, current) => current !== index))}>Удалить</button></div>
          <CoordinateFields title="Стол" value={table.bounds} onChange={(key, value) => editCoordinate(index, "bounds", key, value)} />
          {table.seatingZones.map((zone, zoneIndex) => <div className="zone-config" key={zoneIndex}>
            <div className="table-config-heading"><strong>Зона посадки {zoneIndex + 1}</strong><button type="button" className="text-button danger-text" onClick={() => changeTable(index, current => ({ ...current, seatingZones: current.seatingZones.filter((_, i) => i !== zoneIndex) }))} disabled={table.seatingZones.length === 1}>Удалить зону</button></div>
            <CoordinateFields title={`Зона ${zoneIndex + 1}`} value={zone} onChange={(key, value) => editCoordinate(index, `zone-${zoneIndex}`, key, value)} />
          </div>)}
          <button type="button" className={mode.kind === "add-zone" && mode.tableIndex === index ? "text-button active-tool" : "text-button"} onClick={() => { setMode(mode.kind === "add-zone" && mode.tableIndex === index ? { kind: "idle" } : { kind: "add-zone", tableIndex: index }); canvasRef.current?.scrollIntoView({ behavior: "smooth", block: "center" }); }}>+ Нарисовать зону посадки</button>
        </article>)}
      </div>

      {onNext && (
        <div className="setup-footer" style={{ marginTop: "24px", display: "flex", justifyContent: "flex-end" }}>
          <button
            type="button"
            className="primary-button"
            onClick={onNext}
            disabled={tables.length === 0}
          >
            Далее: Указать заказы к столам →
          </button>
        </div>
      )}
    </section>
  );
}

function CoordinateFields({ title, value, onChange }: { title: string; value: Bounds; onChange(key: keyof Bounds, value: number): void }) {
  return <div className="coordinate-fields" aria-label={`Координаты: ${title}`}>
    {(["x", "y", "width", "height"] as const).map(key => <label key={key}>{({ x: "X", y: "Y", width: "Ширина", height: "Высота" })[key]}<input type="number" min="0" max="1" step="0.01" value={value[key].toFixed(2)} onChange={event => onChange(key, Number(event.target.value))} /></label>)}
  </div>;
}

function style(bounds: Bounds) { return { left: `${bounds.x * 100}%`, top: `${bounds.y * 100}%`, width: `${bounds.width * 100}%`, height: `${bounds.height * 100}%` }; }
function rectangleFrom(start: Point, end: Point): Bounds { return normalize({ x: Math.min(start.x, end.x), y: Math.min(start.y, end.y), width: Math.abs(end.x - start.x), height: Math.abs(end.y - start.y) }); }
function normalize(bounds: Bounds): Bounds { const x = clamp(bounds.x, 0, .99); const y = clamp(bounds.y, 0, .99); return { x, y, width: clamp(bounds.width, .01, 1 - x), height: clamp(bounds.height, .01, 1 - y) }; }
function clamp(value: number, min: number, max: number) { return Math.min(max, Math.max(min, value)); }
