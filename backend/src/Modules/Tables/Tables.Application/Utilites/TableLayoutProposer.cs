using FoodVision.Api.Domain;

namespace FoodVision.Api.Utilites;

public sealed class TableLayoutProposer(int frameWidth, int frameHeight)
{
    private const float TableMatchThreshold = .15f;
    private const float PersonMatchThreshold = .2f;
    private readonly List<TableCandidate> _tables = [];
    private int _frameIndex;

    public void AddFrame(IReadOnlyList<Detection> detections)
    {
        var tables = detections.Where(detection => detection.ClassId == 60).ToArray();
        var people = detections.Where(detection => detection.ClassId == 0).ToArray();

        foreach (var detection in tables)
        {
            TableCandidate? bestMatch = null;
            var bestScore = 0f;

            foreach (var candidate in _tables)
            {
                var score = TableMatchScore(candidate.Bounds, detection.Box);
                if (score >= TableMatchThreshold && score > bestScore)
                {
                    bestScore = score;
                    bestMatch = candidate;
                }
            }

            if (bestMatch is null)
            {
                bestMatch = new TableCandidate();
                _tables.Add(bestMatch);
            }

            bestMatch.AddTable(detection.Box);
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

    public IReadOnlyList<TableConfiguration> Build()
    {
        var minObservations = _frameIndex <= 5 ? 2 : Math.Max(3, (int)(_frameIndex * 0.04));

        var validCandidates = _tables
            .Where(table => table.Observations >= minObservations && IsValidTableSize(table.Bounds))
            .OrderByDescending(table => table.Observations)
            .ToList();

        var clusters = new List<TableCandidate>();
        foreach (var candidate in validCandidates)
        {
            var cluster = clusters.FirstOrDefault(c => ShouldMergeTables(c.Bounds, candidate.Bounds));
            if (cluster is not null)
            {
                cluster.Merge(candidate);
            }
            else
            {
                clusters.Add(candidate);
            }
        }

        bool merged;
        do
        {
            merged = false;
            for (var i = 0; i < clusters.Count; i++)
            {
                for (var j = i + 1; j < clusters.Count; j++)
                {
                    if (ShouldMergeTables(clusters[i].Bounds, clusters[j].Bounds))
                    {
                        clusters[i].Merge(clusters[j]);
                        clusters.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
                if (merged) break;
            }
        } while (merged);

        return clusters
            .OrderBy(table => table.Bounds.Y).ThenBy(table => table.Bounds.X)
            .Select((table, index) => new TableConfiguration($"table-{index + 1}", Normalize(table.Bounds),
                table.BuildSeatingZones().Select(Normalize).ToArray()))
            .ToArray();
    }

    private bool IsValidTableSize(Box box)
    {
        var w = box.Width / frameWidth;
        var h = box.Height / frameHeight;
        var area = w * h;
        return w is >= .02f and <= .70f &&
               h is >= .02f and <= .60f &&
               area is >= .001f and <= .18f;
    }

    private static float IntersectionArea(Box a, Box b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
    }

    private static float IntersectionOverMinArea(Box a, Box b)
    {
        var intersection = IntersectionArea(a, b);
        if (intersection <= 0) return 0f;
        var minArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return minArea > 0 ? intersection / minArea : 0f;
    }

    private static float TableMatchScore(Box a, Box b)
    {
        var iou = Geometry.IoU(a, b);
        var ioMin = IntersectionOverMinArea(a, b);
        if (iou >= .15f) return iou;
        if (ioMin >= .22f) return Math.Max(iou, ioMin * .7f);
        return iou;
    }

    private bool ShouldMergeTables(Box a, Box b)
    {
        var iou = Geometry.IoU(a, b);
        if (iou >= .06f) return true;
        var ioMin = IntersectionOverMinArea(a, b);
        if (ioMin >= .10f) return true;

        var y1 = Math.Max(a.Y, b.Y);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var overlapY = Math.Max(0, y2 - y1);

        var minH = Math.Min(a.Height, b.Height);
        var yOverlapRatio = minH > 0 ? overlapY / minH : 0f;

        var gapX = Math.Max(0, Math.Max(a.X - (b.X + b.Width), b.X - (a.X + a.Width))) / frameWidth;
        var gapY = Math.Max(0, Math.Max(a.Y - (b.Y + b.Height), b.Y - (a.Y + a.Height))) / frameHeight;

        var combinedW = (Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X)) / frameWidth;
        var c1x = (a.X + a.Width / 2) / frameWidth;
        var c1y = (a.Y + a.Height / 2) / frameHeight;
        var c2x = (b.X + b.Width / 2) / frameWidth;
        var c2y = (b.Y + b.Height / 2) / frameHeight;
        var dist = MathF.Sqrt((c1x - c2x) * (c1x - c2x) + (c1y - c2y) * (c1y - c2y));

        if (gapX <= .035f && gapY <= 0.001f && yOverlapRatio >= .70f && combinedW <= .25f && dist <= .13f)
            return true;

        return false;
    }

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
        public Box Bounds => Observations > 0
            ? new Box(_sumX / Observations, _sumY / Observations, _sumWidth / Observations, _sumHeight / Observations)
            : new Box(0, 0, 0, 0);

        public void AddTable(Box box)
        {
            _sumX += box.X; _sumY += box.Y; _sumWidth += box.Width; _sumHeight += box.Height; Observations++;
        }

        public void Merge(TableCandidate other)
        {
            _sumX += other._sumX;
            _sumY += other._sumY;
            _sumWidth += other._sumWidth;
            _sumHeight += other._sumHeight;
            Observations += other.Observations;

            foreach (var otherSeat in other._seats)
            {
                var existingSeat = _seats.FirstOrDefault(s => Geometry.IoU(s.Bounds, otherSeat.Bounds) >= PersonMatchThreshold);
                if (existingSeat is null)
                    _seats.Add(otherSeat);
                else
                    existingSeat.Merge(otherSeat);
            }
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
        public Box Bounds => Observations > 0
            ? new Box(_sumX / Observations, _sumY / Observations, _sumWidth / Observations, _sumHeight / Observations)
            : new Box(0, 0, 0, 0);

        public void Add(Box box, int frameIndex)
        {
            _sumX += box.X; _sumY += box.Y; _sumWidth += box.Width; _sumHeight += box.Height; Observations++;
            _run = _lastFrame == frameIndex - 1 ? _run + 1 : 1;
            LongestRun = Math.Max(LongestRun, _run);
            _lastFrame = frameIndex;
        }

        public void Merge(SeatCandidate other)
        {
            _sumX += other._sumX;
            _sumY += other._sumY;
            _sumWidth += other._sumWidth;
            _sumHeight += other._sumHeight;
            Observations += other.Observations;
            LongestRun = Math.Max(LongestRun, other.LongestRun);
        }
    }
}
