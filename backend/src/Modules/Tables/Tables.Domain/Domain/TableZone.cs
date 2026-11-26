namespace FoodVision.Tables.Domain;

public sealed class TableZone
{
    public string Id { get; private set; } = null!;
    public string RestaurantId { get; private set; } = "default";
    public string Name { get; private set; } = null!;
    public int Capacity { get; private set; } = 4;
    public TableGeometry Geometry { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TableZone() { }

    public TableZone(string id, string name, int capacity, TableGeometry geometry, string restaurantId = "default")
    {
        Id = id;
        Name = name;
        Capacity = capacity;
        Geometry = geometry;
        RestaurantId = restaurantId;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Update(string name, int capacity, TableGeometry geometry, bool isActive = true)
    {
        Name = name;
        Capacity = capacity;
        Geometry = geometry;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
