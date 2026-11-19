namespace FoodVision.Api.Domain;

public sealed record TableConfiguration(string Id, NormalizedBounds Bounds, IReadOnlyList<NormalizedBounds> SeatingZones);
public sealed record NormalizedBounds(double X, double Y, double Width, double Height);
