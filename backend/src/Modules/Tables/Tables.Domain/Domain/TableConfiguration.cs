namespace FoodVision.Api.Domain;

public sealed record TableConfiguration(string Id, NormalizedBounds Bounds, IReadOnlyList<NormalizedBounds> SeatingZones);
public sealed record NormalizedBounds(double X, double Y, double Width, double Height);

public sealed record TableOrderInterval(
    string TableId,
    double StartSeconds,
    double? EndSeconds = null,
    string? OrderNumber = null,
    int GuestsCount = 1);
