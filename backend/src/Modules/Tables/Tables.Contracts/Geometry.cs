namespace FoodVision.Api.Utilites;

public sealed record Box(float X, float Y, float Width, float Height);
public sealed record Detection(Box Box, float Score, int ClassId, float HipX, float HipY, bool HasHip, bool Seated);

public static class Geometry
{
    public static bool Overlaps(Box a, Box b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    public static float IoU(Box a, Box b)
    {
        var intersection = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X)) *
                           Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
        return intersection / Math.Max(.001f, a.Width * a.Height + b.Width * b.Height - intersection);
    }
}
