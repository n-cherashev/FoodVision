import { useMemo } from "react";
import type { DetectedTable, OrderDetails, StateSegment, TableState } from "../types";

const labels: Record<string, string> = {
  occupied: "гости за столом",
  occupied_with_items: "гостей нет, стол занят вещами",
  available: "гостей и вещей нет",
};

export function normalizeVisualSegments(segments: StateSegment[]): StateSegment[] {
  if (!segments || segments.length === 0) return [];

  let lastState: TableState = "available";
  const resolved: StateSegment[] = [];

  for (const seg of segments) {
    const effectiveState: TableState = seg.state === "review" ? lastState : seg.state;
    lastState = effectiveState;
    resolved.push({
      startSeconds: seg.startSeconds,
      endSeconds: seg.endSeconds,
      state: effectiveState,
    });
  }

  // Merge adjacent segments that share the same state
  const merged: StateSegment[] = [];
  for (const seg of resolved) {
    const prev = merged[merged.length - 1];
    if (prev && prev.state === seg.state) {
      prev.endSeconds = seg.endSeconds;
    } else {
      merged.push({ ...seg });
    }
  }

  return merged;
}

interface TimelineProps {
  table: DetectedTable;
  index: number;
  duration: number;
  orders?: OrderDetails[];
  onSeek(seconds: number): void;
}

export function Timeline({ table, index, duration, orders = [], onSeek }: TimelineProps) {
  const safeDuration = duration || 1;
  const tableOrders = orders.filter((o) => o.tableId === table.id);
  const visualSegments = useMemo(() => normalizeVisualSegments(table.segments), [table.segments]);
  const service = table.serviceMetrics;

  return (
    <article className="timeline">
      <div className="timeline-heading">
        <div className="heading-left">
          <h3>Стол {index + 1}</h3>
          {tableOrders.length > 0 && (
            <span className="table-orders-badge">
              {tableOrders.length} {tableOrders.length === 1 ? "заказ" : "заказа"}
            </span>
          )}
          {service && (
            <>
              <span className="service-badge" title="Количество зафиксированных подходов официанта">
                🤵 {service.totalApproachesCount} {formatApproaches(service.totalApproachesCount)}
              </span>
              {service.firstSeatedSeconds != null && (
                <>
                  {service.timeToFirstContactSeconds != null ? (
                    <span
                      className={`sla-badge ${service.isFirstContactSlaViolated ? "sla-violation" : "sla-ok"}`}
                      title={`Посадка: ${formatTime(service.firstSeatedSeconds)}, 1-й подход официанта: ${formatTime(service.firstContactSeconds ?? 0)}`}
                    >
                      ⏱️ 1-й контакт: {formatTime(service.timeToFirstContactSeconds)}
                    </span>
                  ) : service.isFirstContactSlaViolated ? (
                    <span className="sla-badge sla-violation" title="Официант не подошел более чем за 5 минут!">
                      ⚠️ SLA &gt;5 мин
                    </span>
                  ) : (
                    <span className="sla-badge sla-waiting" title="Ожидание первого подхода официанта">
                      ⏱️ Ожидание
                    </span>
                  )}
                </>
              )}
            </>
          )}
        </div>
        <span>{table.id}</span>
      </div>
      <div className="bar" aria-label={`Состояния: стол ${index + 1}`}>
        {visualSegments.map((segment, segmentIndex) => (
          <button
            className={`segment ${segment.state.replaceAll("_", "-")}`}
            key={`${segment.startSeconds}-${segmentIndex}`}
            title={`${labels[segment.state] || labels.available}: ${formatTime(segment.startSeconds)}–${formatTime(segment.endSeconds)}`}
            style={{
              left: `${(segment.startSeconds / safeDuration) * 100}%`,
              width: `${Math.max(0, ((segment.endSeconds - segment.startSeconds) / safeDuration) * 100)}%`,
            }}
            onClick={() => onSeek(segment.startSeconds)}
          >
            <span className="sr-only">{labels[segment.state] || labels.available}</span>
          </button>
        ))}

        {/* Waiter Approach Pins on Timeline */}
        {service?.approaches.map((approach, appIdx) => (
          <button
            type="button"
            key={`approach-pin-${approach.startSeconds}-${appIdx}`}
            className="timeline-pin waiter-pin"
            style={{
              left: `${Math.min(99, (approach.startSeconds / safeDuration) * 100)}%`,
            }}
            title={`🤵 Подход официанта: ${formatTime(approach.startSeconds)}–${formatTime(approach.endSeconds)} (${Math.round(approach.durationSeconds)} с)`}
            onClick={(e) => {
              e.stopPropagation();
              onSeek(approach.startSeconds);
            }}
          >
            🤵
          </button>
        ))}

        {/* Order Pins on Timeline */}
        {tableOrders.map((order) => {
          return (
            <div key={order.orderId}>
              {order.videoPositionSeconds != null && (
                <button
                  type="button"
                  className="timeline-pin order-pin"
                  style={{
                    left: `${Math.min(99, (order.videoPositionSeconds / safeDuration) * 100)}%`,
                  }}
                  title={`Заказ ${order.orderNumber || ""}: открыт в ${formatTime(order.videoPositionSeconds)}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSeek(order.videoPositionSeconds!);
                  }}
                >
                  📋
                </button>
              )}

              {/* Paid pin */}
              {order.paidVideoPositionSeconds != null && (
                <button
                  type="button"
                  className="timeline-pin paid-pin"
                  style={{
                    left: `${Math.min(99, (order.paidVideoPositionSeconds / safeDuration) * 100)}%`,
                  }}
                  title={`Заказ ${order.orderNumber || ""}: оплачен в ${formatTime(order.paidVideoPositionSeconds)}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSeek(order.paidVideoPositionSeconds!);
                  }}
                >
                  💳
                </button>
              )}

              {/* Closed pin */}
              {order.closedVideoPositionSeconds != null && (
                <button
                  type="button"
                  className="timeline-pin close-pin"
                  style={{
                    left: `${Math.min(99, (order.closedVideoPositionSeconds / safeDuration) * 100)}%`,
                  }}
                  title={`Заказ ${order.orderNumber || ""}: закрыт в ${formatTime(order.closedVideoPositionSeconds)}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSeek(order.closedVideoPositionSeconds!);
                  }}
                >
                  🏁
                </button>
              )}

              {/* Dish served pins */}
              {order.items
                .filter((item) => (item.status === "Served" || item.status === 3) && item.videoPositionSeconds != null)
                .map((item) => (
                  <button
                    type="button"
                    key={item.itemId}
                    className="timeline-pin dish-pin"
                    style={{
                      left: `${Math.min(99, (item.videoPositionSeconds! / safeDuration) * 100)}%`,
                    }}
                    title={`Подано: ${item.dishName} (${formatTime(item.videoPositionSeconds!)})`}
                    onClick={(e) => {
                      e.stopPropagation();
                      onSeek(item.videoPositionSeconds!);
                    }}
                  >
                    🍽️
                  </button>
                ))}
            </div>
          );
        })}
      </div>

      <div className="timeline-detail">
        {visualSegments.map((segment, segmentIndex) => (
          <button key={`${segment.startSeconds}-${segmentIndex}`} onClick={() => onSeek(segment.startSeconds)}>
            <i className={segment.state.replaceAll("_", "-")} /> {formatTime(segment.startSeconds)} — {labels[segment.state] || labels.available}
          </button>
        ))}

        {/* Waiter approaches summary tags */}
        {service?.approaches.map((app, appIdx) => (
          <button
            key={`app-chip-${app.startSeconds}-${appIdx}`}
            className="service-detail-chip"
            onClick={() => onSeek(app.startSeconds)}
            title={`Перейти к подходу персонала (${Math.round(app.durationSeconds)} с)`}
          >
            🤵 Подход #{appIdx + 1}: {formatTime(app.startSeconds)} ({Math.round(app.durationSeconds)} с)
          </button>
        ))}

        {/* Orders summary tags */}
        {tableOrders.map((order) => (
          <button
            key={order.orderId}
            className="order-detail-chip"
            onClick={() => order.videoPositionSeconds != null && onSeek(order.videoPositionSeconds)}
          >
            📋 {order.orderNumber || "Заказ"}: {order.totalAmount} ₽ ({order.items.length} блюд)
          </button>
        ))}
      </div>
    </article>
  );
}

function formatApproaches(count: number): string {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod100 >= 11 && mod100 <= 19) return "подходов";
  if (mod10 === 1) return "подход";
  if (mod10 >= 2 && mod10 <= 4) return "подхода";
  return "подходов";
}

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds || 0));
  const minutes = Math.floor(safe / 60);
  const remainder = (safe % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
