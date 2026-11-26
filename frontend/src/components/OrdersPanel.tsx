import { useState } from "react";
import {
  addOrderItem,
  cancelOrder,
  closeOrder,
  openOrder,
  payOrder,
  serveOrderItem,
} from "../api";
import type { DetectedTable, OrderDetails, OrderStatus } from "../types";

interface OrdersPanelProps {
  sessionId: string;
  tables: DetectedTable[];
  orders: OrderDetails[];
  currentTime: number;
  onSeek(seconds: number): void;
  onRefreshOrders(): Promise<void> | void;
}

export function OrdersPanel({
  sessionId,
  tables,
  orders,
  currentTime,
  onSeek,
  onRefreshOrders,
}: OrdersPanelProps) {
  const [selectedTableId, setSelectedTableId] = useState<string>("all");
  const [isCreating, setIsCreating] = useState(false);
  const [addingItemOrderId, setAddingItemOrderId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // New order form fields
  const [formTableId, setFormTableId] = useState<string>(tables[0]?.id ?? "");
  const [formOrderNumber, setFormOrderNumber] = useState<string>(
    `№${orders.length + 1}`
  );
  const [formGuestsCount, setFormGuestsCount] = useState<number>(2);
  const [formWaiterName, setFormWaiterName] = useState<string>("Официант");
  const [formTimeSeconds, setFormTimeSeconds] = useState<number>(
    Math.floor(currentTime)
  );

  // New dish form fields
  const [dishName, setDishName] = useState<string>("");
  const [dishCategory, setDishCategory] = useState<string>("Горячее");
  const [dishPrice, setDishPrice] = useState<number>(450);
  const [dishQuantity, setDishQuantity] = useState<number>(1);

  const filteredOrders =
    selectedTableId === "all"
      ? orders
      : orders.filter((o) => o.tableId === selectedTableId);

  function startCreate() {
    setFormTableId(tables[0]?.id ?? "");
    setFormOrderNumber(`№${orders.length + 1}`);
    setFormGuestsCount(2);
    setFormWaiterName("Официант");
    setFormTimeSeconds(Math.floor(currentTime));
    setIsCreating(true);
    setError(null);
  }

  async function handleCreateOrder(e: React.FormEvent) {
    e.preventDefault();
    if (!formTableId) return;
    setLoading(true);
    setError(null);
    try {
      const orderId = `ord-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 6)}`;
      await openOrder({
        orderId,
        tableId: formTableId,
        sessionId,
        orderNumber: formOrderNumber,
        guestsCount: formGuestsCount,
        waiterName: formWaiterName,
        videoPositionSeconds: formTimeSeconds,
      });
      setIsCreating(false);
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось открыть заказ");
    } finally {
      setLoading(false);
    }
  }

  async function handleAddItem(orderId: string, e: React.FormEvent) {
    e.preventDefault();
    if (!dishName.trim()) return;
    setLoading(true);
    setError(null);
    try {
      const itemId = `item-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 6)}`;
      await addOrderItem(orderId, {
        itemId,
        dishName: dishName.trim(),
        category: dishCategory,
        price: Number(dishPrice) || 0,
        quantity: Number(dishQuantity) || 1,
        videoPositionSeconds: Math.floor(currentTime),
      });
      setAddingItemOrderId(null);
      setDishName("");
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось добавить блюдо");
    } finally {
      setLoading(false);
    }
  }

  async function handleServeItem(orderId: string, itemId: string) {
    setLoading(true);
    setError(null);
    try {
      await serveOrderItem(orderId, itemId, Math.floor(currentTime));
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось отметить подачу");
    } finally {
      setLoading(false);
    }
  }

  async function handlePayOrder(orderId: string) {
    setLoading(true);
    setError(null);
    try {
      await payOrder(orderId, undefined, Math.floor(currentTime));
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось оплатить заказ");
    } finally {
      setLoading(false);
    }
  }

  async function handleCloseOrder(orderId: string) {
    setLoading(true);
    setError(null);
    try {
      await closeOrder(orderId, Math.floor(currentTime));
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось закрыть заказ");
    } finally {
      setLoading(false);
    }
  }

  async function handleCancelOrder(orderId: string) {
    setLoading(true);
    setError(null);
    try {
      await cancelOrder(orderId, "Отменено пользователем", Math.floor(currentTime));
      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось отменить заказ");
    } finally {
      setLoading(false);
    }
  }

  async function handleCreateDemoOrder() {
    if (!tables.length) return;
    setLoading(true);
    setError(null);
    try {
      const targetTable = tables[0];
      const orderId = `ord-demo-${Date.now().toString(36)}`;
      await openOrder({
        orderId,
        tableId: targetTable.id,
        sessionId,
        orderNumber: `№${orders.length + 1}`,
        guestsCount: 2,
        waiterName: "Официант",
        videoPositionSeconds: Math.min(10, Math.floor(currentTime)),
      });

      // Add 2 dishes
      await addOrderItem(orderId, {
        itemId: `dish-1-${Date.now()}`,
        dishName: "Паста Карбонара",
        category: "Паста",
        price: 550,
        quantity: 1,
        course: 1,
        videoPositionSeconds: 15,
      });

      await addOrderItem(orderId, {
        itemId: `dish-2-${Date.now()}`,
        dishName: "Чай облепиховый",
        category: "Напитки",
        price: 250,
        quantity: 1,
        course: 1,
        videoPositionSeconds: 15,
      });

      await onRefreshOrders();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось создать демо-заказ");
    } finally {
      setLoading(false);
    }
  }

  return (
    <section className="orders-panel" aria-labelledby="orders-heading">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Модуль заказов</p>
          <h2 id="orders-heading">Пометки и события заказов</h2>
        </div>
        <div className="orders-actions">
          <button
            type="button"
            className="secondary-button"
            onClick={handleCreateDemoOrder}
            disabled={loading || tables.length === 0}
            title="Создать пример заказа со связанными блюдами"
          >
            + Демо-заказ
          </button>
          <button
            type="button"
            className="primary-button"
            onClick={startCreate}
            disabled={loading || tables.length === 0}
          >
            + Отметить заказ на {formatTime(currentTime)}
          </button>
        </div>
      </div>

      {error && <p className="message error" role="alert">{error}</p>}

      {/* Tables filter */}
      <div className="orders-filter-row">
        <span>Фильтр по столу:</span>
        <button
          type="button"
          className={`filter-chip ${selectedTableId === "all" ? "active" : ""}`}
          onClick={() => setSelectedTableId("all")}
        >
          Все столы ({orders.length})
        </button>
        {tables.map((t, index) => {
          const count = orders.filter((o) => o.tableId === t.id).length;
          return (
            <button
              type="button"
              key={t.id}
              className={`filter-chip ${selectedTableId === t.id ? "active" : ""}`}
              onClick={() => setSelectedTableId(t.id)}
            >
              Стол {index + 1} ({count})
            </button>
          );
        })}
      </div>

      {/* Creation Modal / Inline Form */}
      {isCreating && (
        <form className="order-form-card" onSubmit={handleCreateOrder}>
          <h3>Новый заказ к столу</h3>
          <div className="form-grid">
            <label>
              Стол
              <select
                value={formTableId}
                onChange={(e) => setFormTableId(e.target.value)}
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
                value={formOrderNumber}
                onChange={(e) => setFormOrderNumber(e.target.value)}
                required
              />
            </label>
            <label>
              Гостей
              <input
                type="number"
                min="1"
                max="20"
                value={formGuestsCount}
                onChange={(e) => setFormGuestsCount(Number(e.target.value))}
              />
            </label>
            <label>
              Официант
              <input
                type="text"
                value={formWaiterName}
                onChange={(e) => setFormWaiterName(e.target.value)}
              />
            </label>
            <label>
              Секунда на видео
              <input
                type="number"
                step="0.5"
                min="0"
                value={formTimeSeconds}
                onChange={(e) => setFormTimeSeconds(Number(e.target.value))}
              />
            </label>
          </div>
          <div className="form-actions">
            <button
              type="button"
              className="secondary-button"
              onClick={() => setIsCreating(false)}
            >
              Отмена
            </button>
            <button
              type="submit"
              className="primary-button"
              disabled={loading}
            >
              {loading ? "Сохранение..." : "Открыть заказ"}
            </button>
          </div>
        </form>
      )}

      {/* Orders List */}
      <div className="orders-cards-grid">
        {filteredOrders.length === 0 ? (
          <div className="orders-empty">
            <p>Заказов пока не отмечено.</p>
            <p className="subtitle">
              Поставьте видео на паузу в момент, когда официант принимает заказ, и
              нажмите «+ Отметить заказ на видео».
            </p>
          </div>
        ) : (
          filteredOrders.map((order) => {
            const tableIndex = tables.findIndex((t) => t.id === order.tableId);
            const tableName = tableIndex >= 0 ? `Стол ${tableIndex + 1}` : order.tableId;
            const statusLabel = getStatusLabel(order.status);
            const statusClass = getStatusClass(order.status);

            return (
              <article key={order.orderId} className={`order-card ${statusClass}`}>
                <div className="order-card-header">
                  <div>
                    <span className="order-table-badge">{tableName}</span>
                    <strong className="order-number-title">
                      {order.orderNumber || "Заказ"}
                    </strong>
                    <span className="order-waiter">
                      {order.waiterName} • {order.guestsCount} гостей
                    </span>
                  </div>
                  <div className="order-header-right">
                    <span className={`order-status-badge ${statusClass}`}>
                      {statusLabel}
                    </span>
                    <strong className="order-amount">{order.totalAmount} ₽</strong>
                  </div>
                </div>

                {/* Video events & milestones */}
                <div className="order-milestones">
                  {order.videoPositionSeconds != null && (
                    <button
                      type="button"
                      className="milestone-chip"
                      onClick={() => onSeek(order.videoPositionSeconds!)}
                      title="Перемотать видео на открытие заказа"
                    >
                      📋 Открыт: {formatTime(order.videoPositionSeconds)}
                    </button>
                  )}

                  {order.firstDishServedAt && (
                    <span className="milestone-chip served">
                      🍽️ Первое блюдо подано
                    </span>
                  )}

                  {order.paidAt && (
                    <button
                      type="button"
                      className="milestone-chip paid"
                      onClick={() => order.paidVideoPositionSeconds != null && onSeek(order.paidVideoPositionSeconds)}
                      title={order.paidVideoPositionSeconds != null ? "Перемотать на момент оплаты" : undefined}
                      disabled={order.paidVideoPositionSeconds == null}
                    >
                      💳 Оплачен: {order.paidAmount || order.totalAmount} ₽
                      {order.paidVideoPositionSeconds != null && ` (${formatTime(order.paidVideoPositionSeconds)})`}
                    </button>
                  )}

                  {order.closedAt && (
                    <button
                      type="button"
                      className="milestone-chip closed"
                      onClick={() => order.closedVideoPositionSeconds != null && onSeek(order.closedVideoPositionSeconds)}
                      title={order.closedVideoPositionSeconds != null ? "Перемотать на момент закрытия заказа" : undefined}
                      disabled={order.closedVideoPositionSeconds == null}
                    >
                      🏁 Закрыт{order.closedVideoPositionSeconds != null && `: ${formatTime(order.closedVideoPositionSeconds)}`}
                    </button>
                  )}
                </div>

                {/* Items list */}
                <div className="order-items-section">
                  <div className="order-items-header">
                    <h4>Позиции заказа ({order.items.length})</h4>
                    <button
                      type="button"
                      className="text-button"
                      onClick={() =>
                        setAddingItemOrderId(
                          addingItemOrderId === order.orderId ? null : order.orderId
                        )
                      }
                    >
                      {addingItemOrderId === order.orderId ? "Скрыть" : "+ Добавить блюдо"}
                    </button>
                  </div>

                  {addingItemOrderId === order.orderId && (
                    <form
                      className="add-dish-form"
                      onSubmit={(e) => handleAddItem(order.orderId, e)}
                    >
                      <input
                        type="text"
                        placeholder="Название блюда"
                        value={dishName}
                        onChange={(e) => setDishName(e.target.value)}
                        required
                      />
                      <input
                        type="text"
                        placeholder="Категория"
                        value={dishCategory}
                        onChange={(e) => setDishCategory(e.target.value)}
                      />
                      <input
                        type="number"
                        placeholder="Цена"
                        value={dishPrice}
                        onChange={(e) => setDishPrice(Number(e.target.value))}
                      />
                      <input
                        type="number"
                        placeholder="Кол-во"
                        min="1"
                        value={dishQuantity}
                        onChange={(e) => setDishQuantity(Number(e.target.value))}
                      />
                      <button type="submit" className="secondary-button" disabled={loading}>
                        Добавить
                      </button>
                    </form>
                  )}

                  <ul className="order-items-list">
                    {order.items.map((item) => {
                      const isServed = item.status === "Served" || item.status === 3;
                      return (
                        <li key={item.itemId} className="order-item-row">
                          <div className="item-info">
                            <strong>{item.dishName}</strong>
                            <span>
                              {item.quantity} × {item.price} ₽
                            </span>
                            {item.category && <small>({item.category})</small>}
                          </div>
                          <div className="item-status-actions">
                            {isServed ? (
                              <button
                                type="button"
                                className="item-served-badge"
                                onClick={() =>
                                  item.videoPositionSeconds != null &&
                                  onSeek(item.videoPositionSeconds)
                                }
                                title="Перемотать на момент подачи блюда"
                              >
                                ✓ Подано
                                {item.videoPositionSeconds != null &&
                                  ` (${formatTime(item.videoPositionSeconds)})`}
                              </button>
                            ) : (
                              <button
                                type="button"
                                className="serve-button"
                                onClick={() => handleServeItem(order.orderId, item.itemId)}
                                disabled={loading}
                                title="Зафиксировать подачу блюда на текущей секунде видео"
                              >
                                Подать на {formatTime(currentTime)}
                              </button>
                            )}
                          </div>
                        </li>
                      );
                    })}
                  </ul>
                </div>

                {/* Order actions footer */}
                <div className="order-card-actions">
                  {(order.status === "Open" || order.status === 0) && (
                    <>
                      <button
                        type="button"
                        className="secondary-button"
                        onClick={() => handlePayOrder(order.orderId)}
                        disabled={loading}
                        title="Зафиксировать оплату заказа на текущей секунде видео"
                      >
                        💳 Оплатить ({formatTime(currentTime)})
                      </button>
                      <button
                        type="button"
                        className="danger-text text-button"
                        onClick={() => handleCancelOrder(order.orderId)}
                        disabled={loading}
                      >
                        Отменить заказ
                      </button>
                    </>
                  )}
                  {(order.status === "Paid" || order.status === 1) && (
                    <button
                      type="button"
                      className="primary-button"
                      onClick={() => handleCloseOrder(order.orderId)}
                      disabled={loading}
                      title="Зафиксировать освобождение стола и закрытие счета"
                    >
                      🏁 Закрыть заказ ({formatTime(currentTime)})
                    </button>
                  )}
                </div>
              </article>
            );
          })
        )}
      </div>
    </section>
  );
}

function getStatusLabel(status: OrderStatus): string {
  if (status === "Open" || status === 0) return "Открыт";
  if (status === "Paid" || status === 1) return "Оплачен";
  if (status === "Closed" || status === 2) return "Закрыт";
  if (status === "Cancelled" || status === 3) return "Отменён";
  return String(status);
}

function getStatusClass(status: OrderStatus): string {
  if (status === "Open" || status === 0) return "open";
  if (status === "Paid" || status === 1) return "paid";
  if (status === "Closed" || status === 2) return "closed";
  if (status === "Cancelled" || status === 3) return "cancelled";
  return "open";
}

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds || 0));
  const minutes = Math.floor(safe / 60);
  const remainder = (safe % 60).toString().padStart(2, "0");
  return `${minutes}:${remainder}`;
}
