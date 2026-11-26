import { useCallback, useEffect, useMemo, useState } from "react";
import { fetchTableZones, getOrdersBySession } from "./api";
import { SetupEditor } from "./components/SetupEditor";
import { VideoViewer } from "./components/VideoViewer";
import { OrdersPanel } from "./components/OrdersPanel";
import type { DetectedTable, OrderDetails, TableZone } from "./types";

type AppPage = "stream" | "editor" | "orders";

export default function App() {
  const [page, setPage] = useState<AppPage>("stream");
  const [sessionId] = useState<string>("live-stream");
  const [orders, setOrders] = useState<OrderDetails[]>([]);
  const [zones, setZones] = useState<TableZone[]>([]);
  const [currentTime, setCurrentTime] = useState<number>(0);

  const refreshOrders = useCallback(async () => {
    try {
      const list = await getOrdersBySession(sessionId);
      setOrders(list);
    } catch {
      // Session might not have orders yet
    }
  }, [sessionId]);

  const refreshZones = useCallback(async () => {
    try {
      const list = await fetchTableZones();
      setZones(list);
    } catch {
      // Zones might not be initialized yet
    }
  }, []);

  useEffect(() => {
    void refreshOrders();
    void refreshZones();
    const interval = setInterval(() => {
      void refreshOrders();
    }, 4000);
    return () => clearInterval(interval);
  }, [refreshOrders, refreshZones]);

  const detectedTables: DetectedTable[] = useMemo(() => {
    return zones.map((z) => {
      const xs = z.displayPolygon.map((p) => p[0]);
      const ys = z.displayPolygon.map((p) => p[1]);
      const minX = xs.length ? Math.min(...xs) : 0;
      const maxX = xs.length ? Math.max(...xs) : 100;
      const minY = ys.length ? Math.min(...ys) : 0;
      const maxY = ys.length ? Math.max(...ys) : 100;

      return {
        id: z.name || z.id,
        bounds: {
          x: minX,
          y: minY,
          width: Math.max(maxX - minX, 10),
          height: Math.max(maxY - minY, 10),
        },
        segments: [
          {
            startSeconds: 0,
            endSeconds: 999999,
            state: "available",
          },
        ],
      };
    });
  }, [zones]);

  return (
    <main>
      <header className="main-app-header">
        <div className="brand-header">
          <p className="eyebrow">FoodVision AI · Restaurant Analytics</p>
          <h1>Умная видеоаналитика зала</h1>
        </div>
        <nav className="app-tabs" aria-label="Режим приложения">
          <button
            type="button"
            className={page === "stream" ? "selected" : ""}
            onClick={() => setPage("stream")}
          >
            📡 AI-Трансляция зала
          </button>
          <button
            type="button"
            className={page === "editor" ? "selected" : ""}
            onClick={() => {
              setPage("editor");
              void refreshZones();
            }}
          >
            📐 Разметка и редактор столов
          </button>
          <button
            type="button"
            className={page === "orders" ? "selected" : ""}
            onClick={() => setPage("orders")}
          >
            📋 Заказы зала ({orders.length})
          </button>
        </nav>
      </header>

      {page === "editor" && <SetupEditor />}

      {page === "stream" && (
        <section className="results" aria-labelledby="results-heading">
          <div className="section-heading">
            <div>
              <p className="eyebrow">Мониторинг зала в реальном времени</p>
              <h2 id="results-heading">AI Видеопоток со Supervision разметкой</h2>
            </div>
            <div className="legend">
              <span><i className="available" /> Свободен (0)</span>
              <span><i className="review" /> Частично занят</span>
              <span><i className="occupied" /> Полная посадка</span>
              <span><span className="legend-emoji">🤵</span> Официант у стола</span>
            </div>
          </div>

          <VideoViewer
            source="http://localhost:5000/stream"
            tables={detectedTables}
            orders={orders}
            currentTime={currentTime}
            onTimeUpdate={setCurrentTime}
          />

          <OrdersPanel
            sessionId={sessionId}
            tables={detectedTables}
            orders={orders}
            currentTime={currentTime}
            onSeek={setCurrentTime}
            onRefreshOrders={refreshOrders}
          />
        </section>
      )}

      {page === "orders" && (
        <section className="results">
          <OrdersPanel
            sessionId={sessionId}
            tables={detectedTables}
            orders={orders}
            currentTime={currentTime}
            onSeek={setCurrentTime}
            onRefreshOrders={refreshOrders}
          />
        </section>
      )}
    </main>
  );
}
