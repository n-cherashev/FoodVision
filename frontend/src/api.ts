import type {
  AddOrderItemPayload,
  AnalysisResult,
  OpenOrderPayload,
  OrderDetails,
  TableConfiguration,
  TableOrderInterval,
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

