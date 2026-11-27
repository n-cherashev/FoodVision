import type {
  AddOrderItemPayload,
  AnalysisResult,
  OpenOrderPayload,
  OrderDetails,
  RestaurantStatus,
  TableConfiguration,
  TableOrderInterval,
  TableZone,
} from "./types";

const apiUrl = import.meta.env.VITE_API_URL ?? "http://localhost:8080";

async function postVideo(
  path: string,
  video: File,
  signal: AbortSignal,
  layout?: TableConfiguration[],
  orders?: TableOrderInterval[],
  sessionId?: string
) {
  const form = new FormData();
  form.append("video", video);
  if (layout) form.append("layout", JSON.stringify(layout));
  if (orders && orders.length > 0) form.append("orders", JSON.stringify(orders));
  if (sessionId) form.append("sessionId", sessionId);

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

async function jsonRequest<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`${apiUrl}${endpoint}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(options.headers ?? {}),
    },
  });

  if (!response.ok) {
    const detail = await response.text();
    let message = detail;
    try {
      const payload = JSON.parse(detail) as { error?: string };
      message = payload.error ?? detail;
    } catch {
      // Keep fallback
    }
    throw new Error(message || `Запрос завершился с ошибкой ${response.status}.`);
  }

  return response.json() as Promise<T>;
}

export async function prepareTables(video: File, signal: AbortSignal): Promise<TableConfiguration[]> {
  const result = (await postVideo("/prepare", video, signal)) as { tables: TableConfiguration[] };
  return result.tables;
}

export async function analyzeVideo(
  video: File,
  layout: TableConfiguration[],
  orders: TableOrderInterval[],
  signal: AbortSignal,
  sessionId?: string
): Promise<AnalysisResult> {
  return postVideo("", video, signal, layout, orders, sessionId) as Promise<AnalysisResult>;
}

export async function openOrder(payload: OpenOrderPayload): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>("/api/orders/open", {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function addOrderItem(orderId: string, payload: AddOrderItemPayload): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(`/api/orders/${encodeURIComponent(orderId)}/items`, {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export async function serveOrderItem(
  orderId: string,
  itemId: string,
  videoPositionSeconds?: number
): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(
    `/api/orders/${encodeURIComponent(orderId)}/items/${encodeURIComponent(itemId)}/serve`,
    {
      method: "POST",
      body: JSON.stringify({ videoPositionSeconds, servedAt: new Date().toISOString() }),
    }
  );
}

export async function payOrder(
  orderId: string,
  amount?: number,
  videoPositionSeconds?: number
): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(`/api/orders/${encodeURIComponent(orderId)}/pay`, {
    method: "POST",
    body: JSON.stringify({ amount, videoPositionSeconds, paidAt: new Date().toISOString() }),
  });
}

export async function closeOrder(
  orderId: string,
  videoPositionSeconds?: number
): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(`/api/orders/${encodeURIComponent(orderId)}/close`, {
    method: "POST",
    body: JSON.stringify({ videoPositionSeconds, closedAt: new Date().toISOString() }),
  });
}

export async function cancelOrder(
  orderId: string,
  reason: string,
  videoPositionSeconds?: number
): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(`/api/orders/${encodeURIComponent(orderId)}/cancel`, {
    method: "POST",
    body: JSON.stringify({ reason, videoPositionSeconds, cancelledAt: new Date().toISOString() }),
  });
}

export async function getOrdersByTable(tableId: string): Promise<OrderDetails[]> {
  return jsonRequest<OrderDetails[]>(`/api/orders/table/${encodeURIComponent(tableId)}`);
}

export async function getOrdersBySession(sessionId: string): Promise<OrderDetails[]> {
  return jsonRequest<OrderDetails[]>(`/api/orders/session/${encodeURIComponent(sessionId)}`);
}

export async function getOrderById(orderId: string): Promise<OrderDetails> {
  return jsonRequest<OrderDetails>(`/api/orders/${encodeURIComponent(orderId)}`);
}

const analyticsUrl = import.meta.env.VITE_ANALYTICS_URL ?? "http://localhost:5000";

export async function fetchCurrentFrame(time?: number): Promise<{ url: string; time: number; width: number; height: number }> {
  const query = time !== undefined ? `?time=${time}` : "";
  const res = await fetch(`${analyticsUrl}/api/video/frame${query}`);
  if (!res.ok) throw new Error("Не удалось получить кадр с видеопотока.");
  const frameTime = parseFloat(res.headers.get("X-Video-Time") || "0");
  const width = parseInt(res.headers.get("X-Frame-Width") || "1280", 10);
  const height = parseInt(res.headers.get("X-Frame-Height") || "720", 10);
  const blob = await res.blob();
  return { url: URL.createObjectURL(blob), time: frameTime, width, height };
}

export async function uploadVideoFile(file: File): Promise<{ success: boolean; filename: string }> {
  const form = new FormData();
  form.append("file", file);
  const res = await fetch(`${analyticsUrl}/api/video/upload`, {
    method: "POST",
    body: form,
  });
  if (!res.ok) throw new Error("Не удалось загрузить видеофайл.");
  return res.json();
}

export async function autoDetectTables(time?: number, fileBlob?: Blob | File): Promise<TableZone[]> {
  let res: Response;
  if (fileBlob) {
    const form = new FormData();
    form.append("file", fileBlob, "frame.jpg");
    res = await fetch(`${analyticsUrl}/api/tables/autodetect`, {
      method: "POST",
      body: form,
    });
  } else {
    const query = time !== undefined ? `?time=${time}` : "";
    res = await fetch(`${analyticsUrl}/api/tables/autodetect${query}`, { method: "POST" });
  }
  if (!res.ok) throw new Error("Ошибка автоопределения столов.");
  const data = await res.json();
  return (data.tables || []).map((t: any) => ({
    id: t.id || t.name,
    name: t.name,
    capacity: t.max || t.capacity || 4,
    displayPolygon: t.display_polygon || [],
    countingPolygon: t.counting_polygon || [],
    isActive: true,
  }));
}

export async function fetchTableZones(): Promise<TableZone[]> {
  try {
    const data = await jsonRequest<any[]>("/api/tables/zones");
    if (data && data.length > 0) {
      return data.map((z: any) => ({
        id: z.id,
        name: z.name,
        capacity: z.capacity,
        displayPolygon: (z.displayPolygon || []).map((p: any) => Array.isArray(p) ? p : [p.x, p.y]),
        countingPolygon: (z.countingPolygon || []).map((p: any) => Array.isArray(p) ? p : [p.x, p.y]),
        isActive: z.isActive ?? true,
      }));
    }
  } catch {
    // fallback to analytics
  }

  const res = await fetch(`${analyticsUrl}/api/tables/zones`);
  const list = await res.json();
  return list.map((z: any) => ({
    id: z.name,
    name: z.name,
    capacity: z.max || 4,
    displayPolygon: z.display_polygon || [],
    countingPolygon: z.counting_polygon || [],
    isActive: true,
  }));
}

export async function saveTableZones(zones: TableZone[]): Promise<void> {
  const dtos = zones.map(z => ({
    id: z.id,
    name: z.name,
    capacity: z.capacity,
    displayPolygon: z.displayPolygon.map(([x, y]) => ({ x, y })),
    countingPolygon: z.countingPolygon.map(([x, y]) => ({ x, y })),
    isActive: z.isActive ?? true,
  }));

  try {
    await jsonRequest("/api/tables/zones", {
      method: "PUT",
      body: JSON.stringify(dtos),
    });
  } catch (err) {
    console.warn("Backend save zones error, syncing directly to analytics:", err);
  }

  await fetch(`${analyticsUrl}/api/tables/reload`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(zones.map(z => ({
      name: z.name,
      max: z.capacity,
      display_polygon: z.displayPolygon,
      counting_polygon: z.countingPolygon,
    }))),
  });
}

export async function fetchRestaurantStatus(): Promise<RestaurantStatus> {
  const res = await fetch(`${analyticsUrl}/api/restaurant/status`);
  if (!res.ok) throw new Error("Не удалось получить статус ресторана");
  return res.json();
}

export async function fetchCurrentVideoInfo(): Promise<{ filename: string; path: string; url: string | null }> {
  const res = await fetch(`${analyticsUrl}/api/video/info`);
  if (!res.ok) throw new Error("Не удалось получить информацию о текущем видео");
  const data = await res.json();
  return {
    ...data,
    url: data.url ? `${analyticsUrl}${data.url}` : null,
  };
}

