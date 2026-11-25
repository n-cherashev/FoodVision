using CSharpFunctionalExtensions;
using FoodVision.Orders.Contracts;

namespace FoodVision.Tables.Domain;

public sealed class TableSessionState
{
    private readonly List<TableOrderInfo> _orders = new();
    private readonly List<TableEvent> _uncommittedEvents = new();
    private readonly HashSet<string> _handledActionIds = new();

    public string SessionId { get; private set; } = null!;
    public string TableId { get; private set; } = null!;
    public string RestaurantId { get; private set; } = null!;

    public bool HasPeople { get; private set; }
    public int PeopleCount { get; private set; }
    public bool HasBelongings { get; private set; }
    public bool HasOpenOrders { get; private set; }
    public bool IsOccupied { get; private set; }

    public TableCleaningStatus CleaningStatus { get; private set; }
    public DataFreshness VideoFreshness { get; private set; }
    public DataFreshness OrdersFreshness { get; private set; }

    public double? LastVideoPositionSeconds { get; private set; }
    public DateTimeOffset? LastVideoObservationTime { get; private set; }
    public DateTimeOffset? LastOrderUpdateTime { get; private set; }

    public bool WasUsedInSession { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<TableOrderInfo> Orders => _orders.AsReadOnly();
    public IReadOnlyList<TableEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<string> HandledActionIds => _handledActionIds;

    private TableSessionState() { }

    private TableSessionState(string sessionId, string tableId, string restaurantId)
    {
        SessionId = sessionId;
        TableId = tableId;
        RestaurantId = restaurantId;

        HasPeople = false;
        PeopleCount = 0;
        HasBelongings = false;
        HasOpenOrders = false;
        IsOccupied = false;

        CleaningStatus = TableCleaningStatus.Unknown;
        VideoFreshness = DataFreshness.NoData;
        OrdersFreshness = DataFreshness.NoData;

        WasUsedInSession = false;
        Version = 1;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public static Result<TableSessionState, string> Create(
        string sessionId,
        string tableId,
        string restaurantId = "default")
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Result.Failure<TableSessionState, string>("Идентификатор сессии не может быть пустым.");
        if (string.IsNullOrWhiteSpace(tableId))
            return Result.Failure<TableSessionState, string>("Идентификатор стола не может быть пустым.");
        if (string.IsNullOrWhiteSpace(restaurantId))
            return Result.Failure<TableSessionState, string>("Идентификатор ресторана не может быть пустым.");

        return Result.Success<TableSessionState, string>(new TableSessionState(
            sessionId.Trim(),
            tableId.Trim(),
            restaurantId.Trim()));
    }

    public UnitResult<string> ApplyVideoObservation(
        bool? hasPeople,
        int? peopleCount,
        bool? hasBelongings,
        DataFreshness freshness,
        double videoPositionSeconds,
        DateTimeOffset timestamp,
        string? actionId = null)
    {
        if (!double.IsFinite(videoPositionSeconds) || videoPositionSeconds < 0)
            return UnitResult.Failure<string>("Позиция видео должна быть неотрицательным конечным числом.");

        if (actionId != null && _handledActionIds.Contains(actionId))
            return UnitResult.Success<string>();

        if (freshness != DataFreshness.Fresh)
        {
            if (VideoFreshness != freshness)
            {
                var previousFreshness = VideoFreshness;
                VideoFreshness = freshness;
                UpdatedAt = timestamp;
                RecordEvent(TableEventType.FreshnessChanged, TableEventSource.VideoAnalytics,
                    videoPositionSeconds, timestamp,
                    $"Актуальность данных видеоаналитики изменилась с {previousFreshness} на {freshness}", null);
                Version++;
            }
            if (actionId != null) _handledActionIds.Add(actionId);
            return UnitResult.Success<string>();
        }

        var newHasPeople = hasPeople ?? false;
        var newCount = peopleCount ?? (newHasPeople ? 1 : 0);
        if (newCount < 0)
            return UnitResult.Failure<string>("Количество гостей не может быть отрицательным.");
        if (newHasPeople && newCount == 0)
            return UnitResult.Failure<string>("При наличии гостей их количество должно быть больше нуля.");

        var newHasBelongings = hasBelongings ?? false;

        if (VideoFreshness != DataFreshness.Fresh)
        {
            var previousFreshness = VideoFreshness;
            VideoFreshness = DataFreshness.Fresh;
            RecordEvent(TableEventType.FreshnessChanged, TableEventSource.VideoAnalytics,
                videoPositionSeconds, timestamp,
                $"Актуальность данных видеоаналитики восстановлена: {previousFreshness} -> Fresh", null);
        }

        LastVideoPositionSeconds = videoPositionSeconds;
        LastVideoObservationTime = timestamp;
        UpdatedAt = timestamp;

        if (newHasPeople != HasPeople)
        {
            RecordEvent(newHasPeople ? TableEventType.PeopleArrived : TableEventType.PeopleLeft,
                TableEventSource.VideoAnalytics, videoPositionSeconds, timestamp,
                newHasPeople ? $"Гости сели за стол (человек: {newCount})" : "Гости покинули стол",
                actionId != null ? $"{actionId}:people" : null);
        }
        HasPeople = newHasPeople;
        PeopleCount = newCount;

        if (newHasBelongings != HasBelongings)
        {
            RecordEvent(newHasBelongings ? TableEventType.ItemsPlaced : TableEventType.ItemsRemoved,
                TableEventSource.VideoAnalytics, videoPositionSeconds, timestamp,
                newHasBelongings ? "На столе оставлены вещи" : "Вещи со стола убраны",
                actionId != null ? $"{actionId}:items" : null);
        }
        HasBelongings = newHasBelongings;

        RecalculateOccupancy(videoPositionSeconds, timestamp, TableEventSource.VideoAnalytics);

        if (actionId != null) _handledActionIds.Add(actionId);
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> ApplyOrderEvent(OrderEvent orderEvent)
    {
        if (orderEvent == null)
            return UnitResult.Failure<string>("Событие заказа не может быть null.");

        var eventKey = orderEvent.EventId != Guid.Empty
            ? orderEvent.EventId.ToString()
            : $"{orderEvent.OrderId}:{orderEvent.EventVersion}:{orderEvent.EventType}";

        if (_handledActionIds.Contains(eventKey))
            return UnitResult.Success<string>();

        if (OrdersFreshness != DataFreshness.Fresh)
        {
            OrdersFreshness = DataFreshness.Fresh;
            RecordEvent(TableEventType.FreshnessChanged, TableEventSource.Orders,
                orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                "Данные о заказах актуализированы", null);
        }

        LastOrderUpdateTime = orderEvent.Timestamp;
        UpdatedAt = orderEvent.Timestamp;

        var existingIndex = _orders.FindIndex(o => o.OrderId == orderEvent.OrderId);

        switch (orderEvent.EventType)
        {
            case OrderEventType.Opened:
                if (existingIndex >= 0)
                {
                    _orders[existingIndex].UpdateStatus(OrderStatus.Open, null, orderEvent.EventVersion);
                }
                else
                {
                    var newOrderResult = TableOrderInfo.Create(
                        orderEvent.OrderId,
                        OrderStatus.Open,
                        orderEvent.OpenedAt ?? orderEvent.Timestamp,
                        null,
                        orderEvent.EventVersion);
                    if (newOrderResult.IsFailure)
                        return UnitResult.Failure<string>(newOrderResult.Error);
                    _orders.Add(newOrderResult.Value);
                }
                RecordEvent(TableEventType.OrderOpened, TableEventSource.Orders,
                    orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                    $"Открыт заказ #{orderEvent.OrderId}", eventKey);
                break;

            case OrderEventType.Paid:
                if (existingIndex >= 0)
                {
                    _orders[existingIndex].MarkPaid(orderEvent.PaidAt ?? orderEvent.Timestamp, orderEvent.EventVersion);
                }
                else
                {
                    var newOrderResult = TableOrderInfo.Create(
                        orderEvent.OrderId,
                        OrderStatus.Paid,
                        orderEvent.OpenedAt ?? orderEvent.Timestamp,
                        orderEvent.PaidAt ?? orderEvent.Timestamp,
                        orderEvent.EventVersion);
                    if (newOrderResult.IsFailure)
                        return UnitResult.Failure<string>(newOrderResult.Error);
                    _orders.Add(newOrderResult.Value);
                }
                RecordEvent(TableEventType.OrderPaid, TableEventSource.Orders,
                    orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                    $"Заказ #{orderEvent.OrderId} оплачен (остаётся открытым до закрытия)", eventKey);
                break;

            case OrderEventType.Closed:
                if (existingIndex >= 0)
                {
                    _orders.RemoveAt(existingIndex);
                }
                RecordEvent(TableEventType.OrderClosed, TableEventSource.Orders,
                    orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                    $"Заказ #{orderEvent.OrderId} закрыт", eventKey);
                break;

            case OrderEventType.Cancelled:
                if (existingIndex >= 0)
                {
                    _orders.RemoveAt(existingIndex);
                }
                RecordEvent(TableEventType.OrderCancelled, TableEventSource.Orders,
                    orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                    $"Заказ #{orderEvent.OrderId} отменён", eventKey);
                break;

            case OrderEventType.Transferred:
                if (string.Equals(orderEvent.TableId, TableId, StringComparison.OrdinalIgnoreCase))
                {
                    if (existingIndex >= 0)
                    {
                        _orders.RemoveAt(existingIndex);
                    }
                    RecordEvent(TableEventType.OrderTransferred, TableEventSource.Orders,
                        orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                        $"Заказ #{orderEvent.OrderId} перенесён на стол {orderEvent.TargetTableId}", eventKey);
                }
                else if (string.Equals(orderEvent.TargetTableId, TableId, StringComparison.OrdinalIgnoreCase))
                {
                    if (existingIndex < 0)
                    {
                        var transferredOrderResult = TableOrderInfo.Create(
                            orderEvent.OrderId,
                            orderEvent.Status,
                            orderEvent.OpenedAt ?? orderEvent.Timestamp,
                            orderEvent.PaidAt,
                            orderEvent.EventVersion);
                        if (transferredOrderResult.IsFailure)
                            return UnitResult.Failure<string>(transferredOrderResult.Error);
                        _orders.Add(transferredOrderResult.Value);
                    }
                    RecordEvent(TableEventType.OrderTransferred, TableEventSource.Orders,
                        orderEvent.VideoPositionSeconds, orderEvent.Timestamp,
                        $"Заказ #{orderEvent.OrderId} перенесён со стола {orderEvent.TableId}", eventKey);
                }
                break;

            case OrderEventType.Updated:
                if (existingIndex >= 0)
                {
                    _orders[existingIndex].UpdateStatus(orderEvent.Status, orderEvent.PaidAt, orderEvent.EventVersion);
                }
                break;
        }

        RecalculateOccupancy(orderEvent.VideoPositionSeconds, orderEvent.Timestamp, TableEventSource.Orders);

        _handledActionIds.Add(eventKey);
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> ApplyOrderSnapshot(
        OrdersSnapshot snapshot,
        DataFreshness freshness,
        DateTimeOffset timestamp)
    {
        if (snapshot == null)
            return UnitResult.Failure<string>("Снимок заказов не может быть null.");

        if (freshness != DataFreshness.Fresh)
        {
            if (OrdersFreshness != freshness)
            {
                OrdersFreshness = freshness;
                UpdatedAt = timestamp;
                Version++;
            }
            return UnitResult.Success<string>();
        }

        OrdersFreshness = DataFreshness.Fresh;
        LastOrderUpdateTime = timestamp;
        UpdatedAt = timestamp;

        var tableActiveOrders = snapshot.Orders
            .Where(o => string.Equals(o.TableId, TableId, StringComparison.OrdinalIgnoreCase) &&
                        (o.Status == OrderStatus.Open || o.Status == OrderStatus.Paid))
            .ToList();

        _orders.Clear();
        foreach (var orderDto in tableActiveOrders)
        {
            var orderResult = TableOrderInfo.Create(
                orderDto.OrderId,
                orderDto.Status,
                orderDto.OpenedAt,
                orderDto.PaidAt,
                orderDto.Version);
            if (orderResult.IsSuccess)
                _orders.Add(orderResult.Value);
        }

        RecalculateOccupancy(null, timestamp, TableEventSource.Orders);
        Version++;
        return UnitResult.Success<string>();
    }

    public UnitResult<string> ConfirmCleaning(
        double videoPositionSeconds,
        DateTimeOffset timestamp,
        string? actionId = null)
    {
        if (!double.IsFinite(videoPositionSeconds) || videoPositionSeconds < 0)
            return UnitResult.Failure<string>("Позиция видео должна быть неотрицательным конечным числом.");

        if (actionId != null && _handledActionIds.Contains(actionId))
            return UnitResult.Success<string>();

        if (CleaningStatus == TableCleaningStatus.Ready)
        {
            if (actionId != null) _handledActionIds.Add(actionId);
            return UnitResult.Success<string>();
        }

        if (IsOccupied)
            return UnitResult.Failure<string>("Нельзя подтвердить окончание уборки: стол занят.");

        if (VideoFreshness != DataFreshness.Fresh)
            return UnitResult.Failure<string>("Нельзя подтвердить окончание уборки: данные видеоаналитики отсутствуют или устарели.");

        if (OrdersFreshness != DataFreshness.Fresh)
            return UnitResult.Failure<string>("Нельзя подтвердить окончание уборки: данные о заказах отсутствуют или устарели.");

        CleaningStatus = TableCleaningStatus.Ready;
        UpdatedAt = timestamp;

        RecordEvent(TableEventType.CleaningConfirmed, TableEventSource.Manual,
            videoPositionSeconds, timestamp,
            "Окончание уборки стола подтверждено пользователем вручную", actionId);

        if (actionId != null) _handledActionIds.Add(actionId);
        Version++;
        return UnitResult.Success<string>();
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    public void LoadHandledActions(IEnumerable<string> actionIds)
    {
        foreach (var id in actionIds)
            _handledActionIds.Add(id);
    }

    private void RecalculateOccupancy(
        double? videoPosition,
        DateTimeOffset timestamp,
        TableEventSource source)
    {
        HasOpenOrders = _orders.Any(o => o.IsOpen);

        var previouslyOccupied = IsOccupied;
        var currentlyOccupied = HasPeople || HasBelongings || HasOpenOrders;
        IsOccupied = currentlyOccupied;

        if (currentlyOccupied)
        {
            WasUsedInSession = true;
            if (!previouslyOccupied)
            {
                RecordEvent(TableEventType.OccupancyChanged, source, videoPosition, timestamp,
                    "Стол занят (есть люди, вещи или открытый заказ)", null);
            }
        }
        else
        {
            if (previouslyOccupied)
            {
                RecordEvent(TableEventType.OccupancyChanged, source, videoPosition, timestamp,
                    "Стол освободился (нет людей, вещей и открытых заказов)", null);

                CleaningStatus = TableCleaningStatus.NeedsCleaning;
                RecordEvent(TableEventType.CleaningRequired, source, videoPosition, timestamp,
                    "Стол освобождён после использования и требует уборки", null);
            }
        }
    }

    private void RecordEvent(
        TableEventType eventType,
        TableEventSource source,
        double? videoPositionSeconds,
        DateTimeOffset timestamp,
        string description,
        string? actionId)
    {
        var evtResult = TableEvent.Create(
            Guid.NewGuid(),
            SessionId,
            TableId,
            eventType,
            source,
            videoPositionSeconds,
            timestamp,
            description,
            actionId,
            Version);

        if (evtResult.IsSuccess)
        {
            _uncommittedEvents.Add(evtResult.Value);
        }
    }
}
