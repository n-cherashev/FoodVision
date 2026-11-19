export type TableState = "occupied" | "occupied_with_items" | "available" | "review";

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

export interface TableConfiguration {
  id: string;
  bounds: Bounds;
  seatingZones: Bounds[];
}

export interface AnalysisResult {
  durationSeconds: number;
  videoUrl?: string;
  tables: DetectedTable[];
}
