using System.Runtime.InteropServices;
using BookSlot.Domain.Availability;
using BookSlot.Domain.Staff;

namespace BookSlot.UnitTests.Domain.Availability;

public class AvailabilityEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();

    private static TimeZoneInfo Warsaw =>
        TimeZoneInfo.FindSystemTimeZoneById(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Central European Standard Time" : "Europe/Warsaw");

    private static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    private static AvailabilityRule Rule(DayOfWeek day, int startHour, int endHour)
        => AvailabilityRule.Create(Guid.NewGuid(), TenantId, StaffId, day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0)).Value;

    private static AvailabilityOverride Holiday(DateOnly date)
        => AvailabilityOverride.Unavailable(Guid.NewGuid(), TenantId, StaffId, date, reason: null).Value;

    private static AvailabilityOverride Extra(DateOnly date, int startHour, int endHour)
        => AvailabilityOverride.Window(Guid.NewGuid(), TenantId, StaffId, date, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), reason: null).Value;

    private static AvailabilityRequest Request(
        TimeZoneInfo tz,
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyCollection<AvailabilityRule>? rules = null,
        IReadOnlyCollection<AvailabilityOverride>? overrides = null,
        IReadOnlyCollection<BusyInterval>? busy = null,
        int duration = 60,
        int step = 30,
        int bufBefore = 0,
        int bufAfter = 0,
        int maxConcurrent = 1)
        => new()
        {
            TimeZone = tz,
            FromUtc = from,
            ToUtc = to,
            DurationMinutes = duration,
            SlotIntervalMinutes = step,
            BufferBeforeMinutes = bufBefore,
            BufferAfterMinutes = bufAfter,
            MaxConcurrent = maxConcurrent,
            Rules = rules ?? Array.Empty<AvailabilityRule>(),
            Overrides = overrides ?? Array.Empty<AvailabilityOverride>(),
            Busy = busy ?? Array.Empty<BusyInterval>(),
        };

    [Fact]
    public void Generates_evenly_spaced_slots_across_single_window()
    {
        // Monday 2026-01-05, 09:00–12:00 UTC, 60m service, 30m step → 5 slots (09:00..11:00).
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero);
        var req = Request(Utc, from, to, rules: new[] { Rule(DayOfWeek.Monday, 9, 12) });

        var result = AvailabilityEngine.GenerateSlots(req);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(9, 9, 10, 10, 11);
        result.Value.Select(s => s.StartUtc.Minute).Should().Equal(0, 30, 0, 30, 0);
        result.Value.All(s => s.EndUtc - s.StartUtc == TimeSpan.FromMinutes(60)).Should().BeTrue();
    }

    [Fact]
    public void Returns_empty_when_no_rules_and_no_overrides()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(7);
        var req = Request(Utc, from, to);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Full_day_unavailable_override_suppresses_all_slots_for_that_date()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(2);
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 17), Rule(DayOfWeek.Tuesday, 9, 17) },
            overrides: new[] { Holiday(new DateOnly(2026, 1, 5)) });

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Should().OnlyContain(s => s.StartUtc.Date == new DateTime(2026, 1, 6));
    }

    [Fact]
    public void Extra_window_override_adds_slots_on_a_day_with_no_rule()
    {
        // Saturday — no rule, override adds 10:00–12:00.
        var from = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var req = Request(Utc, from, to,
            overrides: new[] { Extra(new DateOnly(2026, 1, 10), 10, 12) },
            duration: 60, step: 30);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour * 60 + s.StartUtc.Minute)
            .Should().Equal(10 * 60, 10 * 60 + 30, 11 * 60);
    }

    [Fact]
    public void Overlapping_rule_and_override_are_merged_not_duplicated()
    {
        // Rule 09–12 + override extra 11–13 → effective 09–13 → 60m/60m step → 4 slots.
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            overrides: new[] { Extra(new DateOnly(2026, 1, 5), 11, 13) },
            duration: 60, step: 60);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(9, 10, 11, 12);
    }

    [Fact]
    public void Busy_intervals_remove_overlapping_slots_at_default_max_concurrent()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var busy = new[]
        {
            new BusyInterval(from.AddHours(10), from.AddHours(11)),
        };
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            busy: busy,
            duration: 60, step: 30);

        var result = AvailabilityEngine.GenerateSlots(req);

        // 09:00 and 11:00 remain; 09:30, 10:00, 10:30 overlap the busy block.
        result.Value.Select(s => s.StartUtc.Hour * 60 + s.StartUtc.Minute)
            .Should().Equal(9 * 60, 11 * 60);
    }

    [Fact]
    public void Buffer_before_extends_conflict_window_backwards()
    {
        // bufBefore=15, bufAfter=0. Busy [10:00, 11:00].
        // Candidate 09:00 runs 09:00–10:00. Buffered window [08:45, 10:00].
        // Busy start is 10:00 == bufferedEnd → half-open, NO overlap → 09:00 stays.
        // Candidate 09:15 runs 09:15–10:15. Buffered [09:00, 10:15] → overlaps → removed.
        // Candidate 11:00 buffered [10:45, 12:00] → overlaps [10:00, 11:00] (10:45 < 11:00) → removed.
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var busy = new[] { new BusyInterval(from.AddHours(10), from.AddHours(11)) };
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            busy: busy,
            duration: 60, step: 15, bufBefore: 15, bufAfter: 0);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Should().Contain(s => s.StartUtc.Hour == 9 && s.StartUtc.Minute == 0);
        result.Value.Should().NotContain(s => s.StartUtc.Hour == 9 && s.StartUtc.Minute == 15);
        result.Value.Should().NotContain(s => s.StartUtc.Hour == 10);
        result.Value.Should().NotContain(s => s.StartUtc.Hour == 11);
    }

    [Fact]
    public void Max_concurrent_allows_parallel_bookings_until_limit()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var busy = new[]
        {
            new BusyInterval(from.AddHours(10), from.AddHours(11)),
            new BusyInterval(from.AddHours(10), from.AddHours(11)),
        };
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            busy: busy,
            duration: 60, step: 60, maxConcurrent: 3);

        var result = AvailabilityEngine.GenerateSlots(req);

        // 10:00 has 2 overlaps, cap is 3 → still allowed.
        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(9, 10, 11);
    }

    [Fact]
    public void Max_concurrent_blocks_when_overlaps_reach_limit()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var busy = new[]
        {
            new BusyInterval(from.AddHours(10), from.AddHours(11)),
            new BusyInterval(from.AddHours(10), from.AddHours(11)),
        };
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            busy: busy,
            duration: 60, step: 60, maxConcurrent: 2);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(9, 11);
    }

    [Fact]
    public void Local_rules_translate_correctly_to_utc_in_non_utc_timezone()
    {
        // Warsaw CET (winter) = UTC+1. Rule 09–12 local Monday → 08:00–11:00 UTC.
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var req = Request(Warsaw, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            duration: 60, step: 60);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(8, 9, 10);
    }

    [Fact]
    public void Spring_forward_dst_gap_skips_invalid_local_times()
    {
        // Europe/Warsaw spring-forward 2026-03-29: 02:00 CET → 03:00 CEST.
        // Rule Sunday 01:00–05:00 local. Step 30m, duration 30m.
        // 02:00 and 02:30 local are invalid (DST gap) → skipped.
        // 01:30 local is valid but its END (02:00 CET) falls in the gap → also skipped.
        // Valid candidates: 01:00, 03:00, 03:30, 04:00, 04:30 local.
        var from = new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);
        var req = Request(Warsaw, from, to,
            rules: new[] { Rule(DayOfWeek.Sunday, 1, 5) },
            duration: 30, step: 30);

        var result = AvailabilityEngine.GenerateSlots(req);

        // Expected UTC: 01:00 CET=00:00, 03:00 CEST=01:00, 03:30=01:30, 04:00=02:00, 04:30=02:30
        result.Value.Select(s => new { s.StartUtc.Hour, s.StartUtc.Minute })
            .Should().Equal(
                new { Hour = 0, Minute = 0 },
                new { Hour = 1, Minute = 0 },
                new { Hour = 1, Minute = 30 },
                new { Hour = 2, Minute = 0 },
                new { Hour = 2, Minute = 30 });
    }

    [Fact]
    public void Slots_outside_requested_utc_range_are_filtered()
    {
        // Rule 09–12, but request only 10:00–11:30 UTC → only slots fully inside remain.
        var from = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 5, 11, 30, 0, TimeSpan.Zero);
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 12) },
            duration: 60, step: 30);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour * 60 + s.StartUtc.Minute)
            .Should().Equal(10 * 60, 10 * 60 + 30);
    }

    [Fact]
    public void Split_shifts_on_same_day_produce_two_disjoint_groups()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 11), Rule(DayOfWeek.Monday, 14, 16) },
            duration: 60, step: 60);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Select(s => s.StartUtc.Hour).Should().Equal(9, 10, 14, 15);
    }

    [Fact]
    public void Invalid_request_returns_failure()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var req = Request(Utc, from, from, duration: 60);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Availability.InvalidRange");
    }

    [Fact]
    public void Returns_empty_when_duration_exceeds_all_windows()
    {
        var from = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        var req = Request(Utc, from, to,
            rules: new[] { Rule(DayOfWeek.Monday, 9, 10) },
            duration: 90, step: 30);

        var result = AvailabilityEngine.GenerateSlots(req);

        result.Value.Should().BeEmpty();
    }
}
