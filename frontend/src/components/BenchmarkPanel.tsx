import { useEffect, useRef, useState } from "react";

type StreamMetric = { id: number; status: string; accepted?: number; processed?: number; skipped?: number; dropped?: number; overBudget?: number; tableCount?: number; tableStates?: Record<string, number>; objectDetections?: number; personDetections?: number; cpuPercent?: number; memoryMb?: number; deliveredFps?: number; analyticsFps?: number; queueWaitAvgMs?: number; queueWaitP95Ms?: number; queueWaitMaxMs?: number; inferenceAvgMs?: number; inferenceP95Ms?: number; inferenceMaxMs?: number; preprocessingAvgMs?: number; tableInferenceAvgMs?: number; poseInferenceAvgMs?: number; personInferenceAvgMs?: number; latencyAvgMs?: number; latencyP95Ms?: number; latencyMaxMs?: number; clientLatencyAvgMs?: number; clientLatencyP95Ms?: number; clientLatencyMaxMs?: number; error?: string };
type RunInfo = { startedAt?: string; appVersion?: string; sourceFps?: number; durationSeconds?: number; elapsedSeconds?: number; sourceResolution?: string; videoFps?: number; analyticsFps?: number; gpuConcurrency?: number; resolution?: string; model?: string; gpu?: string; videoMemory?: string };
const apiUrl = import.meta.env.VITE_API_URL ?? "http://localhost:8080";
const streamCount = 50;
const streamsPerPage = 10;

export function BenchmarkPanel() {
  const [metrics, setMetrics] = useState<StreamMetric[]>(Array.from({ length: streamCount }, (_, index) => ({ id: index + 1, status: "idle" })));
  const [info, setInfo] = useState<RunInfo>({});
  const [connected, setConnected] = useState(false);
  const [running, setRunning] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const socketRef = useRef<WebSocket | null>(null);
  const canvases = useRef<Array<HTMLCanvasElement | null>>([]);
  const clientSamples = useRef<Array<number[]>>(Array.from({ length: streamCount }, () => []));

  useEffect(() => () => socketRef.current?.close(), []);

  useEffect(() => {
    const socket = socketRef.current;
    if (socket?.readyState === WebSocket.OPEN) {
      socket.send(JSON.stringify({ action: "view", start: page * streamsPerPage + 1, count: streamsPerPage }));
    }
  }, [connected, page]);

  function connect() {
    if (socketRef.current?.readyState === WebSocket.OPEN) return;
    setError(null);
    const url = `${apiUrl.replace(/^http/, "ws")}/api/benchmark`;
    const socket = new WebSocket(url);
    socket.binaryType = "arraybuffer";
    socketRef.current = socket;
    socket.onopen = () => setConnected(true);
    socket.onclose = () => { setConnected(false); setRunning(false); setPreparing(false); };
    socket.onerror = () => setError("Не удалось подключиться к бенчмарку. Проверьте, что backend запущен.");
    socket.onmessage = async event => {
      if (typeof event.data !== "string") {
        const packet = new Uint8Array(event.data as ArrayBuffer);
        const id = packet[1] - 1;
        const width = packet[2] | packet[3] << 8;
        const height = packet[4] | packet[5] << 8;
        const sentAt = Number(new DataView(packet.buffer, packet.byteOffset, packet.byteLength).getBigInt64(6, true));
        const canvas = canvases.current[id];
        if (!canvas) return;
        canvas.width = width; canvas.height = height;
        const context = canvas.getContext("2d");
        if (context) {
          context.putImageData(new ImageData(new Uint8ClampedArray(packet.buffer, packet.byteOffset + 14, width * height * 4), width, height), 0, 0);
          const samples = clientSamples.current[id];
          samples.push(Math.max(0, Date.now() - sentAt));
          const sorted = [...samples].sort((a, b) => a - b);
          const average = samples.reduce((sum, value) => sum + value, 0) / samples.length;
          const p95 = sorted[Math.ceil(.95 * sorted.length) - 1];
          setMetrics(previous => previous.map(item => item.id === id + 1 ? { ...item, clientLatencyAvgMs: average, clientLatencyP95Ms: p95, clientLatencyMaxMs: sorted[sorted.length - 1] } : item));
        }
        return;
      }
      const message = JSON.parse(event.data) as Record<string, unknown>;
      if (message.type === "fatal") { setError(String(message.error)); setPreparing(false); }
      else if (message.type === "preparing") setPreparing(true);
      else if (message.type === "run-start") { setPreparing(false); setInfo(message as RunInfo); }
      else if (message.type === "run-end") setInfo(previous => ({ ...previous, elapsedSeconds: Number(message.elapsedSeconds) }));
      else if (message.type === "stream") setMetrics(previous => previous.map(item => item.id === message.id ? { ...item, ...message } as StreamMetric : item));
      else if (message.type === "run-complete") { setRunning(false); setPreparing(false); }
    };
  }

  function start() {
    clientSamples.current = Array.from({ length: streamCount }, () => []);
    setMetrics(Array.from({ length: streamCount }, (_, index) => ({ id: index + 1, status: "starting" })));
    setPage(0);
    setPreparing(true);
    setRunning(true);
    if (socketRef.current?.readyState === WebSocket.OPEN) socketRef.current.send(JSON.stringify({ action: "start" }));
  }

  function stop() { socketRef.current?.send(JSON.stringify({ action: "stop" })); }
  async function copyResults() {
    try { await navigator.clipboard.writeText(JSON.stringify({ parameters: info, streams: metrics }, null, 2)); setError("Результаты скопированы в буфер обмена."); }
    catch { setError("Браузер не разрешил копирование. Скопируйте показатели со страницы."); }
  }
  const active = metrics.filter(metric => metric.status === "running").length;
  const done = metrics.filter(metric => metric.status === "completed" || metric.status === "error").length;
  const pageCount = Math.ceil(metrics.length / streamsPerPage);
  const firstVisibleIndex = page * streamsPerPage;
  const visibleMetrics = metrics.slice(firstVisibleIndex, firstVisibleIndex + streamsPerPage);

  return <section className="benchmark-panel" aria-labelledby="benchmark-title">
    <div className="section-heading"><div><p className="eyebrow">Локальный нагрузочный прогон</p><h2 id="benchmark-title">{streamCount} независимых видеопотоков</h2><p className="subtitle">Источник: videos/Sample_Video_1.mp4. Каждый поток декодирует файл отдельно и проходит отдельный инференс.</p></div></div>
    <div className="benchmark-controls">
      {!connected ? <button type="button" onClick={connect}>Подключиться</button> : <span className="connection-state">Backend подключён</span>}
      <button type="button" disabled={!connected || running} onClick={start}>Запустить бенчмарк</button>
      <button type="button" className="secondary-button" disabled={!running} onClick={stop}>Остановить</button>
      <button type="button" className="secondary-button" onClick={() => void copyResults()}>Скопировать результат</button>
      <button type="button" className="secondary-button" onClick={() => { socketRef.current?.close(); }}>Отключиться</button>
    </div>
    {error && <p className="message error" role="alert">{error}</p>}
    {preparing && <p className="message loading" role="status">Подготавливаем и прогреваем модели. Видео начнётся после подготовки.</p>}
    <div className="benchmark-summary"><strong>Активно: {active}</strong><strong>Завершено: {done}/{streamCount}</strong><span>Старт: {info.startedAt ? new Date(info.startedAt).toLocaleString() : "—"}</span><span>Длительность теста: {info.elapsedSeconds?.toFixed(1) ?? (running ? "выполняется" : "—")} с</span><span>Приложение: {info.appVersion ?? "—"}</span><span>Источник: {info.sourceResolution ?? "—"}, {info.sourceFps?.toFixed(2) ?? "—"} кадр/с, {info.durationSeconds?.toFixed(1) ?? "—"} с</span><span>Доставка: {info.videoFps ?? "—"} кадр/с, {info.resolution ?? "—"} · аналитика: {info.analyticsFps ?? "—"} кадр/с</span><span>Модель: {info.model ?? "—"}</span><span>GPU / VRAM: {info.gpu ?? "нет данных"} · {info.videoMemory ?? "нет данных"}</span></div>
    <p>Одновременных обработчиков аналитики: {info.gpuConcurrency ?? "—"}</p>
    <div className="benchmark-grid">{visibleMetrics.map((metric, index) => <article className="benchmark-stream" key={metric.id}>
      <div className="stream-title"><h3>Поток {metric.id}</h3><span className={`stream-status ${metric.status}`}>{metric.status}</span></div>
      <canvas ref={element => { canvases.current[firstVisibleIndex + index] = element; }} aria-label={`Видео потока ${metric.id}`} />
      {metric.error && <p className="stream-error">{metric.error}</p>}
      <div className="stream-stats"><span>Доставлено: {metric.deliveredFps?.toFixed(1) ?? "—"} кадр/с · инференс: {metric.analyticsFps?.toFixed(1) ?? "—"} кадр/с</span><span>Принято / обработано / не выбрано аналитикой: {metric.accepted ?? 0} / {metric.processed ?? 0} / {metric.skipped ?? 0} · заменено при перегрузке: {metric.dropped ?? 0} · инференс вне временного бюджета: {metric.overBudget ?? 0}</span><span>Результат аналитики: столов {metric.tableCount ?? "—"} {metric.tableStates && Object.entries(metric.tableStates).map(([state, count]) => `${state}: ${count}`).join(" · ")}</span><span>Детекций на последнем обработанном кадре: объекты {metric.objectDetections ?? "—"} · люди {metric.personDetections ?? "—"}</span><span>Backend CPU / RAM: {metric.cpuPercent?.toFixed(1) ?? "—"}% / {metric.memoryMb?.toFixed(0) ?? "—"} МБ</span><span>GPU queue wait avg / p95 / max: {metric.queueWaitAvgMs?.toFixed(0) ?? "—"} / {metric.queueWaitP95Ms?.toFixed(0) ?? "—"} / {metric.queueWaitMaxMs?.toFixed(0) ?? "—"} мс</span><span>Подготовка кадра / YOLO11x объекты / YOLO11x pose / YOLO26l люди: {metric.preprocessingAvgMs?.toFixed(0) ?? "—"} / {metric.tableInferenceAvgMs?.toFixed(0) ?? "—"} / {metric.poseInferenceAvgMs?.toFixed(0) ?? "—"} / {metric.personInferenceAvgMs?.toFixed(0) ?? "—"} мс</span><span>Inference time avg / p95 / max: {metric.inferenceAvgMs?.toFixed(0) ?? "—"} / {metric.inferenceP95Ms?.toFixed(0) ?? "—"} / {metric.inferenceMaxMs?.toFixed(0) ?? "—"} мс</span><span>От запланированного кадра до результата avg / p95 / max: {metric.latencyAvgMs?.toFixed(0) ?? "—"} / {metric.latencyP95Ms?.toFixed(0) ?? "—"} / {metric.latencyMaxMs?.toFixed(0) ?? "—"} мс</span><span>До отрисовки на клиенте avg / p95 / max: {metric.clientLatencyAvgMs?.toFixed(0) ?? "—"} / {metric.clientLatencyP95Ms?.toFixed(0) ?? "—"} / {metric.clientLatencyMaxMs?.toFixed(0) ?? "—"} мс</span></div>
    </article>)}</div>
    <nav className="benchmark-pagination" aria-label="Страницы потоков">
      <button type="button" className="secondary-button" disabled={page === 0} onClick={() => setPage(current => current - 1)}>Назад</button>
      <span>Потоки {firstVisibleIndex + 1}–{Math.min(firstVisibleIndex + streamsPerPage, metrics.length)} из {metrics.length} · страница {page + 1}/{pageCount}</span>
      <button type="button" className="secondary-button" disabled={page === pageCount - 1} onClick={() => setPage(current => current + 1)}>Вперёд</button>
    </nav>
  </section>;
}
