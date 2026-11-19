import { useEffect, useRef, useState } from "react";
import { analyzeVideo, prepareTables } from "./api";
import { SetupEditor } from "./components/SetupEditor";
import { Timeline } from "./components/Timeline";
import { UploadPanel } from "./components/UploadPanel";
import { VideoViewer } from "./components/VideoViewer";
import type { AnalysisResult, TableConfiguration } from "./types";

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [tables, setTables] = useState<TableConfiguration[] | null>(null);
  const [result, setResult] = useState<AnalysisResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => () => { if (source) URL.revokeObjectURL(source); }, [source]);

  function selectFile(nextFile: File | null) {
    setFile(nextFile);
    setTables(null);
    setResult(null);
    setError(null);
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
    void run(async signal => setTables(await prepareTables(file, signal)));
  }

  function analyze() {
    if (!file || !tables?.length) return;
    setResult(null);
    void run(async signal => setResult(await analyzeVideo(file, tables, signal)));
  }

  function changeTables(next: TableConfiguration[]) {
    setTables(next);
    setResult(null);
  }

  function seek(seconds: number) {
    if (!videoRef.current) return;
    videoRef.current.currentTime = seconds;
    void videoRef.current.play();
  }

  return (
    <main>
      <UploadPanel file={file} loading={loading} prepared={tables !== null} analyzing={result !== null} canSubmit={tables === null || tables.length > 0} onFileChange={selectFile} onSubmit={tables === null ? propose : analyze} />
      {error && <p className="message error" role="alert">{error}</p>}
      {loading && <p className="message loading" role="status">{tables === null ? "Ищем столы на видео. Это может занять несколько минут." : "Анализируем подтверждённые столы и зоны посадки."}</p>}
      {tables && source && !result && !loading && <SetupEditor source={source} tables={tables} onChange={changeTables} />}
      {result && source && (
        <section className="results" aria-labelledby="results-heading">
          <div className="section-heading">
            <div><p className="eyebrow">Результат анализа</p><h2 id="results-heading">План видео</h2></div>
            <div className="legend"><span><i className="occupied" /> Гости за столом</span><span><i className="occupied-with-items" /> Вещи на столе</span><span><i className="available" /> Гостей и вещей нет</span><span><i className="review" /> Нужна проверка</span></div>
          </div>
          <button type="button" className="secondary-button edit-layout" onClick={() => setResult(null)}>Изменить разметку</button>
          <VideoViewer ref={videoRef} source={source} tables={result.tables} />
          {result.tables.length === 0 ? (
            <p className="message empty">Столов нет в разметке. Вернитесь к настройке и добавьте столы.</p>
          ) : (
            <div className="timelines">
              <h2>Шкала состояний</h2>
              {result.tables.map((table, index) => <Timeline key={table.id} table={table} index={index} duration={result.durationSeconds} onSeek={seek} />)}
            </div>
          )}
        </section>
      )}
    </main>
  );
}
