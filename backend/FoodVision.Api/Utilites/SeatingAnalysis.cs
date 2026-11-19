using FoodVision.Api.Domain;

namespace FoodVision.Api.Utilites;

// A separate instance is used for each confirmed table. Time is in video seconds.
internal sealed class SeatingAnalysis(Box table, IReadOnlyList<Box> zones)
{
    private readonly List<Observation> _observations = [];
    private double? _lastOccupied;

    public TableState Classify(double time, IReadOnlyList<Detection> people, IReadOnlyList<Detection> belongings, bool tableVisible, bool ambiguous = false)
    {
        var candidates = people.Where(person => zones.Any(zone => Geometry.Overlaps(person.Box, zone))).ToArray();
        var previous = _observations.Where(item => time - item.Time <= 2.5).ToArray();
        _observations.Clear();
        var used = new HashSet<Observation>();
        var occupied = false;
        foreach (var person in candidates)
        {
            var centerX = person.HasHip ? person.HipX : person.Box.X + person.Box.Width / 2;
            var centerY = person.HasHip ? person.HipY : person.Box.Y + person.Box.Height / 2;
            if (!zones.Any(zone => Contains(zone, centerX, centerY)))
            {
                centerX = person.Box.X + person.Box.Width / 2;
                centerY = person.Box.Y + person.Box.Height / 2;
            }
            if (!zones.Any(zone => Contains(zone, centerX, centerY))) continue;
            var match = previous.Where(item => !used.Contains(item) && time - item.Time <= 2.5 &&
                Distance(item.X, item.Y, centerX, centerY) <= Math.Max(person.Box.Width, person.Box.Height) * .2 &&
                Distance(item.AnchorX, item.AnchorY, centerX, centerY) <= Math.Max(person.Box.Width, person.Box.Height) * .3)
                .OrderBy(item => Distance(item.X, item.Y, centerX, centerY)).FirstOrDefault();
            if (match is not null) used.Add(match);
            var observation = new Observation(centerX, centerY, time, match?.Start ?? time, match?.AnchorX ?? centerX, match?.AnchorY ?? centerY);
            _observations.Add(observation);
            if (person.Seated || time - observation.Start >= 8) occupied = true;
        }
        _observations.AddRange(previous.Where(item => !used.Contains(item)));

        if (occupied)
        {
            _lastOccupied = time;
            return TableState.Occupied;
        }
        if (_lastOccupied is double last && time - last <= 4) return TableState.Occupied;
        // A person of uncertain posture may be a guest, so belongings cannot establish their absence.
        if (candidates.Length > 0 || ambiguous) return TableState.Review;
        if (belongings.Any(item => Geometry.Overlaps(item.Box, table) || zones.Any(zone => Geometry.Overlaps(item.Box, zone))))
            return TableState.OccupiedWithItems;
        return tableVisible ? TableState.Available : TableState.Review;
    }

    private static bool Contains(Box box, float x, float y) => x >= box.X && x <= box.X + box.Width && y >= box.Y && y <= box.Y + box.Height;
    private static double Distance(float x1, float y1, float x2, float y2) => Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
    private sealed record Observation(float X, float Y, double Time, double Start, float AnchorX, float AnchorY);
}
