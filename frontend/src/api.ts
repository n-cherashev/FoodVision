import type { AnalysisResult } from "./types";

const apiUrl = import.meta.env.VITE_API_URL ?? "http://localhost:8080";

export async function analyzeVideo(video: File, signal: AbortSignal): Promise<AnalysisResult> {
  const form = new FormData();
  form.append("video", video);
  const response = await fetch(`${apiUrl}/api/analysis`, {
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

  return response.json() as Promise<AnalysisResult>;
}
