import type { DetectedTable, TableState } from "../types";

const labels: Record<TableState, string> = {
  occupied: "занят",
  available: "не занят",
  review: "нужна проверка",
};

interface TimelineProps {
  table: DetectedTable;
  index: number;
  duration: number;
  onSeek(seconds: number): void;
}

export function Timeline({ table, index, duration, onSeek }: TimelineProps) {
  const safeDuration = duration || 1;
  return (
    <article className="timeline">
      <div className="timeline-heading">
        <h3>Стол {index + 1}</h3>
        <span>{table.id}</span>
      </div>
      <div className="bar" aria-label={`Состояния: стол ${index + 1}`}>
        {table.segments.map((segment, segmentIndex) => (
          <button
            className={`segment ${segment.state}`}
            key={`${segment.startSeconds}-${segmentIndex}`}
            title={`${labels[segment.state]}: ${formatTime(segment.startSeconds)}–${formatTime(segment.endSeconds)}`}
            style={{
              left: `${(segment.startSeconds / safeDuration) * 100}%`,
              width: `${Math.max(0, ((segment.endSeconds - segment.startSeconds) / safeDuration) * 100)}%`,
            }}
            onClick={() => onSeek(segment.startSeconds)}
          >
            <span className="sr-only">{labels[segment.state]}</span>
          </button>
        ))}
      </div>
      <div className="timeline-detail">
        {table.segments.map((segment, segmentIndex) => (
          <button key={`${segment.startSeconds}-${segmentIndex}`} onClick={() => onSeek(segment.startSeconds)}>
            <i className={segment.state} /> {formatTime(segment.startSeconds)} — {labels[segment.state]}
          </button>
        ))}
      </div>
    </article>
  );
}

function formatTime(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.floor(seconds % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
