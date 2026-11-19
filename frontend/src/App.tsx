import { useEffect, useRef, useState } from "react";
import { analyzeVideo } from "./api";
import { Timeline } from "./components/Timeline";
import { UploadPanel } from "./components/UploadPanel";
import { VideoViewer } from "./components/VideoViewer";
import type { AnalysisResult } from "./types";

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [result, setResult] = useState<AnalysisResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => () => { if (source) URL.revokeObjectURL(source); }, [source]);

  function selectFile(nextFile: File | null) {
    setFile(nextFile);
    setResult(null);
    setError(null);
    setSource((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return nextFile ? URL.createObjectURL(nextFile) : null;
    });
  }

  async function submit() {
    if (!file) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      setResult(await analyzeVideo(file, controller.signal));
    } catch (reason) {
      if ((reason as DOMException).name !== "AbortError") {
        setError(reason instanceof Error ? reason.message : "Не удалось обработать видео.");
      }
    } finally {
      setLoading(false);
    }
  }

  function seek(seconds: number) {
    if (!videoRef.current) return;
    videoRef.current.currentTime = seconds;
    void videoRef.current.play();
  }

  return (
    <main>
      <UploadPanel file={file} loading={loading} onFileChange={selectFile} onSubmit={submit} />
      {error && <p className="message error" role="alert">{error}</p>}
      {loading && <p className="message loading" role="status">Анализируем столы и состояние посадки. Это может занять несколько минут.</p>}
      {result && source && (
        <section className="results" aria-labelledby="results-heading">
          <div className="section-heading">
            <div><p className="eyebrow">Результат анализа</p><h2 id="results-heading">План видео</h2></div>
            <div className="legend"><span><i className="occupied" /> Занят</span><span><i className="available" /> Не занят</span><span><i className="review" /> Нужна проверка</span></div>
          </div>
          <VideoViewer ref={videoRef} source={source} tables={result.tables} />
          {result.tables.length === 0 ? (
            <p className="message empty">Столы не обнаружены. Проверьте, что камера неподвижна и столы видны с начала записи.</p>
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
