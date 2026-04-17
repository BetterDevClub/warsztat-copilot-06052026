using BookSlot.Domain.Primitives;
using BookSlot.Domain.Staff;

namespace BookSlot.Domain.Availability;

/// <summary>
/// Pure, database-independent algorithm that computes bookable slots for a staff member.
/// Honours weekly rules, per-date overrides (full unavailability + extra windows), buffers,
/// <c>MaxConcurrent</c> and existing busy intervals. Timezone handling uses the provided
/// <see cref="TimeZoneInfo"/>; DST is handled by <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>.
/// </summary>
public static class AvailabilityEngine
{
    /// <summary>Generates bookable UTC slots for the given request.</summary>
    public static Result<IReadOnlyList<AvailabilitySlot>> GenerateSlots(AvailabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = request.Validate();
        if (validation.IsFailure)
        {
            return Result.Failure<IReadOnlyList<AvailabilitySlot>>(validation.Error);
        }

        var tz = request.TimeZone;
        var duration = TimeSpan.FromMinutes(request.DurationMinutes);
        var step = TimeSpan.FromMinutes(request.SlotIntervalMinutes);
        var beforeBuffer = TimeSpan.FromMinutes(request.BufferBeforeMinutes);
        var afterBuffer = TimeSpan.FromMinutes(request.BufferAfterMinutes);

        var localFrom = TimeZoneInfo.ConvertTime(request.FromUtc, tz);
        var localTo = TimeZoneInfo.ConvertTime(request.ToUtc, tz);

        var firstDate = DateOnly.FromDateTime(localFrom.DateTime);
        var lastDate = DateOnly.FromDateTime(localTo.DateTime);

        var overridesByDate = request.Overrides
            .GroupBy(o => o.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rulesByDay = request.Rules
            .GroupBy(r => r.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.ToList());

        var busy = request.Busy.ToList();
        var results = new List<AvailabilitySlot>();

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var windows = ComputeLocalWindows(date, rulesByDay, overridesByDate);
            foreach (var (winStart, winEnd) in windows)
            {
                EmitSlotsForWindow(
                    date, winStart, winEnd, tz, duration, step,
                    beforeBuffer, afterBuffer,
                    request.FromUtc, request.ToUtc, request.MaxConcurrent,
                    busy, results);
            }
        }

        var ordered = results
            .Distinct()
            .OrderBy(s => s.StartUtc)
            .ToList();

        return Result.Success<IReadOnlyList<AvailabilitySlot>>(ordered);
    }

    private static List<(TimeOnly Start, TimeOnly End)> ComputeLocalWindows(
        DateOnly date,
        Dictionary<DayOfWeek, List<AvailabilityRule>> rulesByDay,
        Dictionary<DateOnly, List<AvailabilityOverride>> overridesByDate)
    {
        var dateOverrides = overridesByDate.TryGetValue(date, out var list) ? list : null;

        if (dateOverrides is not null && dateOverrides.Any(o => o.IsUnavailable))
        {
            return new List<(TimeOnly, TimeOnly)>();
        }

        var raw = new List<(TimeOnly Start, TimeOnly End)>();

        if (rulesByDay.TryGetValue(date.DayOfWeek, out var rules))
        {
            foreach (var r in rules)
            {
                raw.Add((r.StartTime, r.EndTime));
            }
        }

        if (dateOverrides is not null)
        {
            foreach (var o in dateOverrides)
            {
                if (!o.IsUnavailable && o.StartTime is { } s && o.EndTime is { } e)
                {
                    raw.Add((s, e));
                }
            }
        }

        return Merge(raw);
    }

    private static List<(TimeOnly Start, TimeOnly End)> Merge(List<(TimeOnly Start, TimeOnly End)> windows)
    {
        if (windows.Count <= 1) return windows;

        var ordered = windows.OrderBy(w => w.Start).ToList();
        var merged = new List<(TimeOnly Start, TimeOnly End)> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = merged[^1];
            var cur = ordered[i];
            if (cur.Start <= prev.End)
            {
                merged[^1] = (prev.Start, cur.End > prev.End ? cur.End : prev.End);
            }
            else
            {
                merged.Add(cur);
            }
        }
        return merged;
    }

    private static void EmitSlotsForWindow(
        DateOnly date,
        TimeOnly winStart,
        TimeOnly winEnd,
        TimeZoneInfo tz,
        TimeSpan duration,
        TimeSpan step,
        TimeSpan beforeBuffer,
        TimeSpan afterBuffer,
        DateTimeOffset requestFromUtc,
        DateTimeOffset requestToUtc,
        int maxConcurrent,
        List<BusyInterval> busy,
        List<AvailabilitySlot> output)
    {
        var localWinStart = date.ToDateTime(winStart);
        var localWinEnd = date.ToDateTime(winEnd);

        for (var localStart = localWinStart;
             localStart + duration <= localWinEnd;
             localStart = localStart + step)
        {
            var localEnd = localStart + duration;

            if (tz.IsInvalidTime(localStart) || tz.IsInvalidTime(localEnd))
            {
                continue;
            }

            DateTimeOffset startUtc;
            DateTimeOffset endUtc;
            try
            {
                startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, tz), TimeSpan.Zero);
                endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, tz), TimeSpan.Zero);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (startUtc < requestFromUtc || endUtc > requestToUtc)
            {
                continue;
            }

            var bufferedStart = startUtc - beforeBuffer;
            var bufferedEnd = endUtc + afterBuffer;

            var overlap = 0;
            foreach (var b in busy)
            {
                if (b.Overlaps(bufferedStart, bufferedEnd))
                {
                    overlap++;
                    if (overlap >= maxConcurrent) break;
                }
            }
            if (overlap >= maxConcurrent) continue;

            output.Add(new AvailabilitySlot(startUtc, endUtc));
        }
    }
}
