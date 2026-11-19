export type TableState = "occupied" | "available" | "review";

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface StateSegment {
  startSeconds: number;
  endSeconds: number;
  state: TableState;
}

export interface DetectedTable {
  id: string;
  bounds: Bounds;
  segments: StateSegment[];
}

export interface AnalysisResult {
  durationSeconds: number;
  videoUrl?: string;
  tables: DetectedTable[];
}
