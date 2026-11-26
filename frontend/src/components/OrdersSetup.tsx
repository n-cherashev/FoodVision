import { useRef, useState } from "react";
import type { TableConfiguration, TableOrderInterval } from "../types";

interface OrdersSetupProps {
  source: string;
  tables: TableConfiguration[];
  orders: TableOrderInterval[];
  loading: boolean;
  onChange(orders: TableOrderInterval[]): void;
  onBack(): void;
  onSubmit(): void;
}

export function OrdersSetup({
  source,
  tables,
  orders,
  loading,
  onChange,
  onBack,
  onSubmit,
}: OrdersSetupProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [videoTime, setVideoTime] = useState<number>(0);

  // Form fields
  const [selectedTableId, setSelectedTableId] = useState<string>(tables[0]?.id ?? "");
  const [orderNumber, setOrderNumber] = useState<string>(`№${orders.length + 1}`);
  const [startSeconds, setStartSeconds] = useState<number>(0);
  const [hasEndSeconds, setHasEndSeconds] = useState<boolean>(true);
  const [endSeconds, setEndSeconds] = useState<number>(60);
  const [guestsCount, setGuestsCount] = useState<number>(2);

  function handleTimeUpdate() {
    if (videoRef.current) {
      setVideoTime(videoRef.current.currentTime);
    }
  }

  function seek(seconds: number) {
    if (videoRef.current) {
      videoRef.current.currentTime = seconds;
      setVideoTime(seconds);
    }
  }

  function handleAddOrder(e: React.FormEvent) {
    e.preventDefault();
    if (!selectedTableId) return;

    const newOrder: TableOrderInterval = {
      tableId: selectedTableId,
      orderNumber: orderNumber.trim() || `№${orders.length + 1}`,
      startSeconds: Math.max(0, Number(startSeconds) || 0),
      endSeconds: hasEndSeconds ? Math.max(Number(startSeconds) || 0, Number(endSeconds) || 0) : null,
      guestsCount: Math.max(1, Number(guestsCount) || 1),
    };

    onChange([...orders, newOrder]);
    setOrderNumber(`№${orders.length + 2}`);
  }

  function handleRemoveOrder(index: number) {
    const next = [...orders];
    next.splice(index, 1);
    onChange(next);
  }

  function handleAddDemoOrder() {
    if (!tables.length) return;
    const demo: TableOrderInterval = {
      tableId: tables[0].id,
      orderNumber: `№${orders.length + 1}`,
      startSeconds: Math.floor(videoTime > 5 ? videoTime : 10),
      endSeconds: Math.floor(videoTime > 5 ? videoTime + 60 : 70),
      guestsCount: 2,
    };
    onChange([...orders, demo]);
  }

  return (
    <section className="orders-setup-container" aria-labelledby="orders-setup-heading">
      <div className="setup-heading">
        <div>
          <p className="eyebrow">Шаг 2 из 2: Заказы к столам</p>
          <h2 id="orders-setup-heading">Задайте интервалы заказов перед анализом</h2>
        </div>
        <div className="orders-actions">
          <button
            type="button"
            className="secondary-button"
            onClick={handleAddDemoOrder}
            disabled={tables.length === 0}
          >
            + Заполнить демо-заказ
          </button>
        </div>
      </div>

      <p className="editor-help">
        Укажите время, когда за столами были открыты заказы (например, по данным кассы).
        Во время открытого заказа стол <strong>100% считается занятым</strong>. После оплаты и закрытия
        заказа стол освободится только тогда, когда за ним <strong>не останется посетителей</strong> (анализируется таймфрейм видео).
      </p>

      {/* Video preview for locating order moments */}
      <div className="orders-setup-grid">
        <div className="orders-setup-video-col">
          <div className="video-frame">
            <video
              ref={videoRef}
              src={source}
              controls
              preload="metadata"
              onTimeUpdate={handleTimeUpdate}
            />
          </div>
          <div className="video-time-indicator">
            <span>Текущий кадр: <strong>{formatTime(videoTime)}</strong> ({videoTime.toFixed(1)} сек)</span>
          </div>
        </div>

        {/* Order entry form */}
        <div className="orders-setup-form-col">
          <form className="order-form-card" onSubmit={handleAddOrder}>
            <h3>Добавить заказ к столу</h3>
            <div className="form-grid">
              <label>
                Стол
                <select
                  value={selectedTableId}
                  onChange={(e) => setSelectedTableId(e.target.value)}
                  required
                >
                  {tables.map((t, idx) => (
                    <option key={t.id} value={t.id}>
                      Стол {idx + 1} ({t.id})
                    </option>
                  ))}
                </select>
              </label>

              <label>
                Номер заказа
                <input
                  type="text"
                  value={orderNumber}
                  onChange={(e) => setOrderNumber(e.target.value)}
                  required
                />
              </label>

              <label>
                Количество гостей
                <input
                  type="number"
                  min="1"
                  max="20"
                  value={guestsCount}
                  onChange={(e) => setGuestsCount(Number(e.target.value))}
                />
              </label>

              <label>
                Время открытия заказа (сек)
                <div className="input-with-button">
                  <input
                    type="number"
                    step="0.5"
                    min="0"
                    value={startSeconds}
                    onChange={(e) => setStartSeconds(Number(e.target.value))}
                    required
                  />
                  <button
                    type="button"
                    className="secondary-button text-tiny"
                    onClick={() => setStartSeconds(Math.floor(videoTime))}
                    title="Вставить текущую секунду видео"
                  >
                    Вставить {formatTime(videoTime)}
                  </button>
                </div>
              </label>

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={hasEndSeconds}
                  onChange={(e) => setHasEndSeconds(e.target.checked)}
                />
                Заказ закрыт / оплачен на видео
              </label>

              {hasEndSeconds && (
                <label>
                  Время закрытия заказа (сек)
                  <div className="input-with-button">
                    <input
                      type="number"
                      step="0.5"
                      min={startSeconds}
                      value={endSeconds}
                      onChange={(e) => setEndSeconds(Number(e.target.value))}
                      required
                    />
                    <button
                      type="button"
                      className="secondary-button text-tiny"
                      onClick={() => setEndSeconds(Math.floor(videoTime))}
                      title="Вставить текущую секунду видео"
                    >
                      Вставить {formatTime(videoTime)}
                    </button>
                  </div>
                </label>
              )}
            </div>

            <div className="form-actions">
              <button type="submit" className="primary-button">
                + Добавить заказ в список
              </button>
            </div>
          </form>

          {/* List of configured orders */}
          <div className="configured-orders-card">
            <h4>Список заказов к видео ({orders.length})</h4>
            {orders.length === 0 ? (
              <p className="message empty text-small">
                Заказов пока не добавлено. Вы можете запустить анализ без заказов или добавить интервалы выше.
              </p>
            ) : (
              <ul className="orders-setup-list">
                {orders.map((o, idx) => {
                  const tableIndex = tables.findIndex((t) => t.id === o.tableId);
                  const tableName = tableIndex >= 0 ? `Стол ${tableIndex + 1}` : o.tableId;

                  return (
                    <li key={`${o.tableId}-${idx}`} className="orders-setup-item">
                      <div className="item-meta">
                        <strong className="order-table-chip">{tableName}</strong>
                        <span className="order-number">{o.orderNumber}</span>
                        <button
                          type="button"
                          className="timecode-link"
                          onClick={() => seek(o.startSeconds)}
                          title="Перемотать видео на открытие"
                        >
                          {formatTime(o.startSeconds)}
                        </button>
                        <span>→</span>
                        {o.endSeconds != null ? (
                          <button
                            type="button"
                            className="timecode-link"
                            onClick={() => seek(o.endSeconds!)}
                            title="Перемотать видео на закрытие"
                          >
                            {formatTime(o.endSeconds)}
                          </button>
                        ) : (
                          <span className="open-badge">открыт до конца</span>
                        )}
                        <span className="guests-count">({o.guestsCount ?? 1} гостей)</span>
                      </div>
                      <button
                        type="button"
                        className="text-button danger-text"
                        onClick={() => handleRemoveOrder(idx)}
                        title="Удалить заказ"
                      >
                        ✕
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </div>
      </div>

      {/* Navigation Footer */}
      <div className="orders-setup-footer">
        <button
          type="button"
          className="secondary-button"
          onClick={onBack}
          disabled={loading}
        >
          ← Назад к разметке столов
        </button>
        <button
          type="button"
          className="primary-button"
          onClick={onSubmit}
          disabled={loading}
        >
          {loading ? "Анализируем видео..." : orders.length > 0
            ? `Запустить анализ видео (с ${orders.length} ${orders.length === 1 ? "заказом" : "заказами"}) →`
            : "Запустить анализ видео (без заказов) →"}
        </button>
      </div>
    </section>
  );
}

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds || 0));
  const minutes = Math.floor(safe / 60);
  const remainder = (safe % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
