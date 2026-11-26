import { useCallback, useEffect, useRef, useState } from "react";
import { analyzeVideo, getOrdersBySession, prepareTables } from "./api";
import { SetupEditor } from "./components/SetupEditor";
import { OrdersSetup } from "./components/OrdersSetup";
import { Timeline } from "./components/Timeline";
import { UploadPanel } from "./components/UploadPanel";
import { VideoViewer } from "./components/VideoViewer";
import { BenchmarkPanel } from "./components/BenchmarkPanel";
import { OrdersPanel } from "./components/OrdersPanel";
import type { AnalysisResult, OrderDetails, TableConfiguration, TableOrderInterval } from "./types";

type WorkflowStep = "upload" | "layout" | "orders" | "results";

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [tables, setTables] = useState<TableConfiguration[] | null>(null);
  const [ordersToAnalyze, setOrdersToAnalyze] = useState<TableOrderInterval[]>([]);
  const [result, setResult] = useState<AnalysisResult | null>(null);
  const [step, setStep] = useState<WorkflowStep>("upload");
  const [sessionId, setSessionId] = useState<string>(() => `sess-${Date.now().toString(36)}`);
  const [orders, setOrders] = useState<OrderDetails[]>([]);
  const [currentTime, setCurrentTime] = useState<number>(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState<"analysis" | "benchmark">("analysis");
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => () => { if (source) URL.revokeObjectURL(source); }, [source]);

  const refreshOrders = useCallback(async () => {
    if (!sessionId) return;
    try {
      const list = await getOrdersBySession(sessionId);
      setOrders(list);
    } catch {
      // Session might not have orders yet
    }
  }, [sessionId]);

  useEffect(() => {
    if (result && sessionId) {
      void refreshOrders();
    }
  }, [result, sessionId, refreshOrders]);

  function selectFile(nextFile: File | null) {
    setFile(nextFile);
    setTables(null);
    setResult(null);
    setOrders([]);
    setOrdersToAnalyze([]);
    setCurrentTime(0);
    setError(null);
    setStep("upload");
    const nextSessionId = `sess-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 6)}`;
    setSessionId(nextSessionId);
    setSource((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return nextFile ? URL.createObjectURL(nextFile) : null;
    });
  }

  async function run(action: (signal: AbortSignal) => Promise<void>) {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    try { await action(controller.signal); }
    catch (reason) {
      if ((reason as DOMException).name !== "AbortError") setError(reason instanceof Error ? reason.message : "Не удалось обработать видео.");
    } finally { setLoading(false); }
  }

  function propose() {
    if (!file) return;
    setResult(null);
    void run(async signal => {
      const proposed = await prepareTables(file, signal);
      setTables(proposed);
      setStep("layout");
    });
  }

  function runAnalysis() {
    if (!file || !tables?.length) return;
    setResult(null);
    void run(async signal => {
      const analysis = await analyzeVideo(file, tables, ordersToAnalyze, signal, sessionId);
      setResult(analysis);
      setStep("results");
    });
  }

  function changeTables(next: TableConfiguration[]) {
    setTables(next);
    setResult(null);
  }

  function seek(seconds: number) {
    if (!videoRef.current) return;
    videoRef.current.currentTime = seconds;
    setCurrentTime(seconds);
    void videoRef.current.play();
  }

  return (
    <main>
      <nav className="app-tabs" aria-label="Режим приложения">
        <button className={page === "analysis" ? "selected" : ""} onClick={() => setPage("analysis")}>Анализ видео</button>
        <button className={page === "benchmark" ? "selected" : ""} onClick={() => setPage("benchmark")}>Бенчмарк</button>
      </nav>
      {page === "benchmark" ? <BenchmarkPanel /> : <>
      <UploadPanel
        file={file}
        loading={loading}
        prepared={tables !== null}
        analyzing={step === "results"}
        canSubmit={step === "upload" ? Boolean(file) : Boolean(tables?.length)}
        onFileChange={selectFile}
        onSubmit={step === "upload" ? propose : () => setStep("orders")}
      />

      {error && <p className="message error" role="alert">{error}</p>}
      {loading && <p className="message loading" role="status">
        {step === "upload"
          ? "Ищем столы на видео. Это может занять некоторое время."
          : "Анализируем видео с учетом разметки столов и интервалов заказов..."}
      </p>}

      {/* Step 1: Layout editing */}
      {step === "layout" && tables && source && !loading && (
        <SetupEditor
          source={source}
          tables={tables}
          onChange={changeTables}
          onNext={() => setStep("orders")}
        />
      )}

      {/* Step 2: Pre-analysis Orders input */}
      {step === "orders" && tables && source && !loading && (
        <OrdersSetup
          source={source}
          tables={tables}
          orders={ordersToAnalyze}
          loading={loading}
          onChange={setOrdersToAnalyze}
          onBack={() => setStep("layout")}
          onSubmit={runAnalysis}
        />
      )}

      {/* Step 3: Analysis Results */}
      {step === "results" && result && source && (
        <section className="results" aria-labelledby="results-heading">
          <div className="section-heading">
            <div><p className="eyebrow">Результат анализа</p><h2 id="results-heading">План видео</h2></div>
            <div className="legend">
              <span><i className="occupied" /> Гости за столом</span>
              <span><i className="occupied-with-items" /> Вещи на столе</span>
              <span><i className="available" /> Гостей и вещей нет</span>
              <span><span className="legend-emoji">📋</span> Заказ</span>
              <span><span className="legend-emoji">🍽️</span> Подача</span>
            </div>
          </div>
          <button type="button" className="secondary-button edit-layout" onClick={() => setStep("orders")}>
            ← Изменить разметку / заказы
          </button>
          <VideoViewer
            ref={videoRef}
            source={source}
            tables={result.tables}
            orders={orders}
            currentTime={currentTime}
            onTimeUpdate={setCurrentTime}
          />
          {result.tables.length === 0 ? (
            <p className="message empty">Столов нет в разметке. Вернитесь к настройке и добавьте столы.</p>
          ) : (
            <div className="timelines">
              <h2>Шкала состояний и заказов</h2>
              {result.tables.map((table, index) => (
                <Timeline
                  key={table.id}
                  table={table}
                  index={index}
                  duration={result.durationSeconds}
                  orders={orders}
                  onSeek={seek}
                />
              ))}
            </div>
          )}

          <OrdersPanel
            sessionId={sessionId}
            tables={result.tables}
            orders={orders}
            currentTime={currentTime}
            onSeek={seek}
            onRefreshOrders={refreshOrders}
          />
        </section>
      )}
      </>}
    </main>
  );
}
