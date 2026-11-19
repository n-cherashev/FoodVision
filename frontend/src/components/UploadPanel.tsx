interface UploadPanelProps {
  file: File | null;
  loading: boolean;
  prepared: boolean;
  analyzing: boolean;
  canSubmit: boolean;
  onFileChange(file: File | null): void;
  onSubmit(): void;
}

export function UploadPanel({ file, loading, prepared, analyzing, canSubmit, onFileChange, onSubmit }: UploadPanelProps) {
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
      <button type="button" onClick={onSubmit} disabled={!file || loading || !canSubmit}>
        {loading ? "Обрабатываем видео…" : prepared ? "Запустить анализ" : "Найти столы"}
      </button>
      {prepared && <p className="flow-step">Шаг 2 из 2 · Проверьте разметку ниже</p>}
      {analyzing && <p className="flow-step">Анализ готов</p>}
    </section>
  );
}
