namespace FoodVision.Api.Utilites;

public sealed record TableAssociation(Box Table, IReadOnlyList<Box> SeatingZones);
public sealed record PersonAssignments(IReadOnlyList<IReadOnlyList<Detection>> People, IReadOnlyList<bool> Ambiguous);

public static class TablePersonAssignment
{
    public static PersonAssignments Assign(
        IReadOnlyList<Detection> people,
        IReadOnlyList<TableAssociation> tables)
    {
        var assignments = Enumerable.Range(0, tables.Count).Select(_ => new List<Detection>()).ToArray();
        var ambiguous = new bool[tables.Count];
        foreach (var person in people)
        {
            var candidates = tables
                .Select((table, index) => new Candidate(index, Score(person, table)))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();
            if (candidates.Length == 0) continue;
            if (candidates.Length > 1 && candidates[0].Score - candidates[1].Score < .03)
            {
                foreach (var candidate in candidates.Where(candidate => candidates[0].Score - candidate.Score < .03)) ambiguous[candidate.Index] = true;
                continue;
            }
            assignments[candidates[0].Index].Add(person);
        }
        return new PersonAssignments(assignments, ambiguous);
    }

    private static double Score(Detection person, TableAssociation table)
    {
        var anchorX = person.HasHip ? person.HipX : person.Box.X + person.Box.Width / 2;
        var anchorY = person.HasHip ? person.HipY : person.Box.Y + person.Box.Height / 2;
        var touchesZone = table.SeatingZones.Any(zone => Geometry.Overlaps(person.Box, zone) || Contains(zone, anchorX, anchorY));
        if (!touchesZone) return 0;

        var dx = Math.Max(0, Math.Max(table.Table.X - anchorX, anchorX - table.Table.X - table.Table.Width));
        var dy = Math.Max(0, Math.Max(table.Table.Y - anchorY, anchorY - table.Table.Y - table.Table.Height));
        var normalizedDistance = Math.Sqrt(dx * dx + dy * dy) / Math.Max(1, Math.Max(person.Box.Width, person.Box.Height));
        return 1 / (1 + normalizedDistance);
    }

    private static bool Contains(Box box, float x, float y) => x >= box.X && x <= box.X + box.Width && y >= box.Y && y <= box.Y + box.Height;
    private sealed record Candidate(int Index, double Score);
}
