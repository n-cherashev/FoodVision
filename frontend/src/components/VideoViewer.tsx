import { forwardRef } from "react";
import type { DetectedTable } from "../types";

interface VideoViewerProps {
  source: string;
  tables: DetectedTable[];
}

export const VideoViewer = forwardRef<HTMLVideoElement, VideoViewerProps>(function VideoViewer(
  { source, tables },
  ref,
) {
  return (
    <div className="video-frame">
      <video ref={ref} src={source} controls preload="metadata" />
      <div className="table-overlay" aria-hidden="true">
        {tables.map((table, index) => (
          <div
            className="table-marker"
            key={table.id}
            style={{
              left: `${table.bounds.x * 100}%`,
              top: `${table.bounds.y * 100}%`,
              width: `${table.bounds.width * 100}%`,
              height: `${table.bounds.height * 100}%`,
            }}
          >
            <span>Стол {index + 1}</span>
          </div>
        ))}
      </div>
    </div>
  );
});
