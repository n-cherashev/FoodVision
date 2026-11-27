import { useEffect, useState } from "react";
import { fetchRestaurantStatus } from "../api";
import type { RestaurantStatus } from "../types";

interface RestaurantStatusPanelProps {
  onSelectTable?(tableId: string): void;
}

export function RestaurantStatusPanel({ onSelectTable }: RestaurantStatusPanelProps) {
  const [status, setStatus] = useState<RestaurantStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showVisualStream, setShowVisualStream] = useState(false);

  useEffect(() => {
    let isMounted = true;

    async function loadStatus() {
      try {
        const data = await fetchRestaurantStatus();
        if (isMounted) {
          setStatus(data);
          setError(null);
          setLoading(false);
        }
      } catch (err) {
        if (isMounted) {
          setError(err instanceof Error ? err.message : String(err));
          setLoading(false);
        }
      }
    }

    void loadStatus();
    const interval = setInterval(() => {
      void loadStatus();
    }, 1000);

    return () => {
      isMounted = false;
      clearInterval(interval);
    };
  }, []);

  const analyticsUrl = import.meta.env.VITE_ANALYTICS_URL || "http://localhost:5000";

  return (
    <div className="restaurant-status-card">
      <div className="restaurant-status-header">
        <div className="status-title-group">
          <h3>📊 Ресторанный статус зала</h3>
          <span className="live-indicator">
            <span className="live-pulse" /> LIVE
          </span>
        </div>
        {status && (
          <div className="status-timer-badge">
            ⏱️ {status.video_time_formatted}
          </div>
        )}
      </div>

      {loading && !status && (
        <div className="status-loading">Загрузка данных ресторана...</div>
      )}

      {error && !status && (
        <div className="status-error">
          ⚠️ Не удалось подключиться к статусу: {error}
        </div>
      )}

      {status && (
        <>
          {/* Key Metrics Grid */}
          <div className="stat-metric-boxes">
            <div className="stat-metric-box">
              <span className="metric-label">🪑 Занятость столов</span>
              <div className="metric-value">
                <strong>{status.occupied_tables}</strong> / {status.total_tables}
                <span className="metric-percent">({status.occupancy_rate}%)</span>
              </div>
              <div className="status-progress-bar">
                <div
                  className="status-progress-fill occupancy-fill"
                  style={{ width: `${Math.min(status.occupancy_rate, 100)}%` }}
                />
              </div>
            </div>

            <div className="stat-metric-box">
              <span className="metric-label">👥 Гостей в зале</span>
              <div className="metric-value">
                <strong>{status.seated_guests}</strong> / {status.total_capacity}
                <span className="metric-percent">({status.capacity_rate}%)</span>
              </div>
              <div className="status-progress-bar">
                <div
                  className="status-progress-fill guests-fill"
                  style={{ width: `${Math.min(status.capacity_rate, 100)}%` }}
                />
              </div>
            </div>
          </div>

          {/* Table by Table Breakdown */}
          <div className="tables-status-section">
            <div className="tables-section-title">
              <span>Столы и таймеры посадки</span>
              <small>{status.tables.length} активных зон</small>
            </div>

            <div className="tables-status-list">
              {status.tables.map((t) => {
                const isFull = t.count >= t.max && t.max > 0;
                const isOccupied = t.count > 0;
                let badgeClass = "badge-free";
                let badgeText = "Свободен";

                if (isFull) {
                  badgeClass = "badge-full";
                  badgeText = "Полная посадка";
                } else if (isOccupied) {
                  badgeClass = "badge-occupied";
                  badgeText = "Занят";
                }

                return (
                  <div
                    key={t.id}
                    className={`table-status-row ${isOccupied ? "is-occupied" : "is-free"}`}
                    onClick={() => onSelectTable?.(t.id)}
                    role="button"
                    tabIndex={0}
                  >
                    <div className="table-row-left">
                      <span className="table-code-badge">{t.name}</span>
                      <span className={`table-state-tag ${badgeClass}`}>{badgeText}</span>
                    </div>

                    <div className="table-row-center">
                      <span className="table-guests-count">
                        <strong>{t.count}</strong> / {t.max} гостей
                      </span>
                    </div>

                    <div className="table-row-right">
                      {isOccupied && t.occupancy_seconds !== null ? (
                        <span className="table-timer-value">
                          ⏱️ {t.occupancy_formatted}
                        </span>
                      ) : (
                        <span className="table-timer-free">⚪ 00:00</span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Optional Visual MJPEG Stream of Dashboard */}
          <div className="dashboard-visual-stream-toggle">
            <button
              type="button"
              className="text-button"
              onClick={() => setShowVisualStream(!showVisualStream)}
            >
              {showVisualStream ? "Скрыть графический MJPEG-дашборд" : "🖼️ Показать графический MJPEG-дашборд (OpenCV)"}
            </button>
            {showVisualStream && (
              <div className="visual-dashboard-wrapper">
                <img
                  src={`${analyticsUrl}/stream/dashboard`}
                  alt="Визуальный дашборд статуса"
                  className="visual-dashboard-img"
                />
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
