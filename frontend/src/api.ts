import type { AnalysisResult, TableConfiguration } from "./types";

const apiUrl = import.meta.env.VITE_API_URL ?? "http://localhost:8080";

async function postVideo(path: string, video: File, signal: AbortSignal, layout?: TableConfiguration[]) {
  const form = new FormData();
  form.append("video", video);
  if (layout) form.append("layout", JSON.stringify(layout));
  const response = await fetch(`${apiUrl}/api/analysis${path}`, {
    method: "POST",
    body: form,
    signal,
  });

  if (!response.ok) {
    const detail = await response.text();
    let message = detail;
    try {
      const payload = JSON.parse(detail) as { error?: string };
      message = payload.error ?? detail;
    } catch {
      // Keep a plain-text server response readable as well.
    }
    throw new Error(message || `Сервер вернул ошибку ${response.status}.`);
  }

  return response.json();
}

export async function prepareTables(video: File, signal: AbortSignal): Promise<TableConfiguration[]> {
  const result = await postVideo("/prepare", video, signal) as { tables: TableConfiguration[] };
  return result.tables;
}

export async function analyzeVideo(video: File, layout: TableConfiguration[], signal: AbortSignal): Promise<AnalysisResult> {
  return postVideo("", video, signal, layout) as Promise<AnalysisResult>;
}
