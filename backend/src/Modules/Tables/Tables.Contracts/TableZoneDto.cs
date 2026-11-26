namespace FoodVision.Tables.Contracts;

public sealed record TablePointDto(double X, double Y);

public sealed record TableZoneDto(
    string Id,
    string Name,
    int Capacity,
    IReadOnlyList<TablePointDto> DisplayPolygon,
    IReadOnlyList<TablePointDto> CountingPolygon,
    bool IsActive = true);
