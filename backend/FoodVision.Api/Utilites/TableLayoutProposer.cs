using FoodVision.Api.Domain;

namespace FoodVision.Api.Utilites;

internal sealed class TableLayoutProposer(int frameWidth, int frameHeight)
{
    private const float TableMatchThreshold = .35f;
    private const float PersonMatchThreshold = .2f;
    private readonly List<TableCandidate> _tables = [];
    private int _frameIndex;

    public void AddFrame(IReadOnlyList<Detection> detections)
    {
        var tables = detections.Where(detection => detection.ClassId == 60).ToArray();
        var people = detections.Where(detection => detection.ClassId == 0).ToArray();
        foreach (var detection in tables)
        {
            var table = _tables.FirstOrDefault(item => Geometry.IoU(item.Bounds, detection.Box) >= TableMatchThreshold);
            if (table is null) { table = new TableCandidate(); _tables.Add(table); }
            table.AddTable(detection.Box);
        }

        foreach (var table in _tables) table.BeginFrame();
        foreach (var person in people)
        {
            var nearby = _tables.Where(table => IsNearTable(person.Box, table.Bounds))
                .OrderBy(table => DistanceToTable(person.Box, table.Bounds)).ToArray();
            if (nearby.Length == 0) continue;
            var best = DistanceToTable(person.Box, nearby[0].Bounds);
            if (nearby.Length > 1 && DistanceToTable(person.Box, nearby[1].Bounds) <= best * 1.2f) continue;
            nearby[0].AddSeat(person.Box, _frameIndex);
        }
        _frameIndex++;
    }

    public IReadOnlyList<TableConfiguration> Build() => _tables
        .Where(table => table.Observations >= 2)
        .OrderBy(table => table.Bounds.Y).ThenBy(table => table.Bounds.X)
        .Select((table, index) => new TableConfiguration($"table-{index + 1}", Normalize(table.Bounds),
            table.BuildSeatingZones().Select(Normalize).ToArray()))
        .ToArray();

    private NormalizedBounds Normalize(Box box)
    {
        var x = Math.Clamp(box.X / frameWidth, 0, .999);
        var y = Math.Clamp(box.Y / frameHeight, 0, .999);
        return new NormalizedBounds(x, y,
            Math.Clamp(box.Width / frameWidth, .001, 1 - x),
            Math.Clamp(box.Height / frameHeight, .001, 1 - y));
    }

    private static bool IsNearTable(Box person, Box table)
    {
        var area = new Box(table.X - table.Width * .75f, table.Y - table.Height * .75f, table.Width * 2.5f, table.Height * 2.5f);
        return Geometry.Overlaps(person, area) && Geometry.IoU(person, table) < .5f;
    }

    private static float DistanceToTable(Box person, Box table)
    {
        var x = person.X + person.Width / 2;
        var y = person.Y + person.Height / 2;
        var dx = Math.Max(0, Math.Max(table.X - x, x - (table.X + table.Width)));
        var dy = Math.Max(0, Math.Max(table.Y - y, y - (table.Y + table.Height)));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private sealed class TableCandidate
    {
        private readonly List<SeatCandidate> _seats = [];
        private readonly HashSet<SeatCandidate> _seatsObservedInFrame = [];
        private float _sumX, _sumY, _sumWidth, _sumHeight;

        public int Observations { get; private set; }
        public Box Bounds => new(_sumX / Observations, _sumY / Observations, _sumWidth / Observations, _sumHeight / Observations);

        public void AddTable(Box box)
        {
            _sumX += box.X; _sumY += box.Y; _sumWidth += box.Width; _sumHeight += box.Height; Observations++;
        }

        public void BeginFrame() => _seatsObservedInFrame.Clear();

        public void AddSeat(Box person, int frameIndex)
        {
            var seat = _seats.FirstOrDefault(item => Geometry.IoU(item.Bounds, person) >= PersonMatchThreshold);
            if (seat is null) { seat = new SeatCandidate(); _seats.Add(seat); }
            if (!_seatsObservedInFrame.Add(seat)) return;
            seat.Add(person, frameIndex);
        }

        public IReadOnlyList<Box> BuildSeatingZones()
        {
            var zones = _seats.Where(seat => seat.LongestRun >= 3).Select(seat => seat.Bounds).ToArray();
            return zones.Length > 0 ? zones : [new Box(Math.Max(0, Bounds.X - Bounds.Width * .4f), Math.Max(0, Bounds.Y - Bounds.Height * .75f), Bounds.Width * 1.8f, Bounds.Height * 2.5f)];
        }
    }

    private sealed class SeatCandidate
    {
        private float _sumX, _sumY, _sumWidth, _sumHeight;
        public int Observations { get; private set; }
        public int LongestRun { get; private set; }
        private int _lastFrame = -2, _run;
        public Box Bounds => new(_sumX / Observations, _sumY / Observations, _sumWidth / Observations, _sumHeight / Observations);
        public void Add(Box box, int frameIndex)
        {
            _sumX += box.X; _sumY += box.Y; _sumWidth += box.Width; _sumHeight += box.Height; Observations++;
            _run = _lastFrame == frameIndex - 1 ? _run + 1 : 1;
            LongestRun = Math.Max(LongestRun, _run);
            _lastFrame = frameIndex;
        }
    }
}
