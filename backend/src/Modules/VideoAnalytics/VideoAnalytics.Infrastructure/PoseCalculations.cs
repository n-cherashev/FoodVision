namespace FoodVision.Api.Utilites;

internal static class PoseCalculations
{
    public static bool HasFlexedKnee((float X, float Y, float Confidence)[] points, int hip, int knee, int ankle)
    {
        var a = points[hip]; var b = points[knee]; var c = points[ankle];
        if (a.Confidence < .2f || b.Confidence < .2f || c.Confidence < .2f) return false;
        var abx = a.X - b.X; var aby = a.Y - b.Y;
        var cbx = c.X - b.X; var cby = c.Y - b.Y;
        var denominator = Math.Sqrt((abx * abx + aby * aby) * (cbx * cbx + cby * cby));
        if (denominator < 1) return false;
        var angle = Math.Acos(Math.Clamp((abx * cbx + aby * cby) / denominator, -1, 1)) * 180 / Math.PI;
        return angle is >= 55 and <= 145;
    }

}
