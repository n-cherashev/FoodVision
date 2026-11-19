interface UploadPanelProps {
  file: File | null;
  loading: boolean;
  onFileChange(file: File | null): void;
  onSubmit(): void;
}

export function UploadPanel({ file, loading, onFileChange, onSubmit }: UploadPanelProps) {
  return (
    <section className="upload-panel" aria-labelledby="upload-heading">
      <div>
        <p className="eyebrow">Анализ записи</p>
        <h1 id="upload-heading">Занятость столов</h1>
        <p className="subtitle">Загрузите видео с неподвижной камеры. Результат появится после завершения обработки.</p>
      </div>
      <label className="file-input">
        <span>Видео</span>
        <input
          type="file"
          accept="video/*"
          disabled={loading}
          onChange={(event) => onFileChange(event.target.files?.[0] ?? null)}
        />
        <strong>{file?.name ?? "Выберите файл"}</strong>
      </label>
      <button type="button" onClick={onSubmit} disabled={!file || loading}>
        {loading ? "Обрабатываем видео…" : "Начать анализ"}
      </button>
    </section>
  );
}
