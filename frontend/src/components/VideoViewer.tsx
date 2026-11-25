import { forwardRef } from "react";
import type { DetectedTable, OrderDetails } from "../types";

interface VideoViewerProps {
  source: string;
  tables: DetectedTable[];
  orders?: OrderDetails[];
  onTimeUpdate?(currentTime: number): void;
}

export const VideoViewer = forwardRef<HTMLVideoElement, VideoViewerProps>(function VideoViewer(
  { source, tables, orders = [], onTimeUpdate },
  ref,
) {
  return (
    <div className="video-frame">
      <video
        ref={ref}
        src={source}
        controls
        preload="metadata"
        onTimeUpdate={(e) => onTimeUpdate?.(e.currentTarget.currentTime)}
      />
      <div className="table-overlay" aria-hidden="true">
        {tables.map((table, index) => {
          const tableOrders = orders.filter((o) => o.tableId === table.id);
          const activeOrder = tableOrders.find(
            (o) => o.status === "Open" || o.status === 0 || o.status === "Paid" || o.status === 1
          );

          return (
            <div
              className={`table-marker ${activeOrder ? "has-active-order" : ""}`}
              key={table.id}
              style={{
                left: `${table.bounds.x * 100}%`,
                top: `${table.bounds.y * 100}%`,
                width: `${table.bounds.width * 100}%`,
                height: `${table.bounds.height * 100}%`,
              }}
            >
              <span>
                Стол {index + 1}
                {activeOrder && (
                  <strong className="table-active-order-badge">
                    📋 {activeOrder.orderNumber || "Заказ"} ({activeOrder.totalAmount} ₽)
                  </strong>
                )}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
});
