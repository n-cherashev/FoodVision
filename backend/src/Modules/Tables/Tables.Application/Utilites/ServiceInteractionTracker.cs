using FoodVision.Api.Domain;

namespace FoodVision.Api.Utilites;

public sealed class ServiceInteractionTracker
{
    private const double MinServeDurationSeconds = 5.0;
    private const double LeaveGraceSeconds = 2.0;

    private readonly List<ServiceApproach> _confirmedApproaches = [];
    private double? _candidateStartSeconds;
    private double? _lastSeenSeconds;

    public void Update(double seconds, bool isStandingPersonNearOccupiedTable)
    {
        if (isStandingPersonNearOccupiedTable)
        {
            if (!_candidateStartSeconds.HasValue)
            {
                _candidateStartSeconds = seconds;
            }
            _lastSeenSeconds = seconds;
        }
        else
        {
            if (_candidateStartSeconds.HasValue && _lastSeenSeconds.HasValue)
            {
                if (seconds - _lastSeenSeconds.Value >= LeaveGraceSeconds)
                {
                    FinalizeCandidate();
                }
            }
        }
    }

    public void Finish(double videoDuration)
    {
        if (_candidateStartSeconds.HasValue && _lastSeenSeconds.HasValue)
        {
            FinalizeCandidate();
        }
    }

    private void FinalizeCandidate()
    {
        if (_candidateStartSeconds.HasValue && _lastSeenSeconds.HasValue)
        {
            var duration = _lastSeenSeconds.Value - _candidateStartSeconds.Value;
            if (duration >= MinServeDurationSeconds)
            {
                _confirmedApproaches.Add(new ServiceApproach(
                    Math.Round(_candidateStartSeconds.Value, 1),
                    Math.Round(_lastSeenSeconds.Value, 1)));
            }
        }
        _candidateStartSeconds = null;
        _lastSeenSeconds = null;
    }

    public TableServiceMetrics BuildMetrics(
        IReadOnlyList<TimeSegment> segments,
        double? manualOrderStartSeconds)
    {
        var firstOccupied = segments
            .FirstOrDefault(s => s.State is TableState.Occupied or TableState.OccupiedWithItems);

        double? firstSeated = firstOccupied is not null ? Math.Round(firstOccupied.StartSeconds, 1) : null;

        var firstApproach = _confirmedApproaches
            .OrderBy(a => a.StartSeconds)
            .FirstOrDefault(a => !firstSeated.HasValue || a.StartSeconds >= firstSeated.Value - 10.0);

        double? firstContact = firstApproach is not null
            ? firstApproach.StartSeconds
            : (manualOrderStartSeconds.HasValue ? Math.Round(manualOrderStartSeconds.Value, 1) : null);

        double? timeToFirstContact = null;
        if (firstSeated.HasValue && firstContact.HasValue)
        {
            timeToFirstContact = Math.Max(0, Math.Round(firstContact.Value - firstSeated.Value, 1));
        }

        var isSlaViolated = timeToFirstContact.HasValue && timeToFirstContact.Value > 300.0;

        var lastOccupied = segments
            .LastOrDefault(s => s.State is TableState.Occupied or TableState.OccupiedWithItems);

        double? totalVisit = null;
        if (firstOccupied is not null && lastOccupied is not null)
        {
            totalVisit = Math.Max(0, Math.Round(lastOccupied.EndSeconds - firstOccupied.StartSeconds, 1));
        }

        return new TableServiceMetrics(
            firstSeated,
            firstContact,
            timeToFirstContact,
            isSlaViolated,
            _confirmedApproaches.Count,
            _confirmedApproaches.ToArray(),
            totalVisit);
    }
}
