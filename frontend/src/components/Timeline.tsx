import type { DetectedTable, OrderDetails, TableState } from "../types";

const labels: Record<TableState, string> = {
  occupied: "гости за столом",
  occupied_with_items: "гостей нет, стол занят вещами",
  available: "гостей и вещей нет",
  review: "нужна проверка",
};

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
        </div>
        <span>{table.id}</span>
      </div>
      <div className="bar" aria-label={`Состояния: стол ${index + 1}`}>
        {table.segments.map((segment, segmentIndex) => (
          <button
            className={`segment ${segment.state.replaceAll("_", "-")}`}
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
        {table.segments.map((segment, segmentIndex) => (
          <button key={`${segment.startSeconds}-${segmentIndex}`} onClick={() => onSeek(segment.startSeconds)}>
            <i className={segment.state.replaceAll("_", "-")} /> {formatTime(segment.startSeconds)} — {labels[segment.state]}
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

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds || 0));
  const minutes = Math.floor(safe / 60);
  const remainder = (safe % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
