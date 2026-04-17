using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Tenants;

/// <summary>
/// Tenant-scoped settings: timezone, default booking window and branding knobs that
/// an <c>Owner</c> tweaks post-registration. Has a 1:1 relationship with <see cref="Tenant"/>
/// — the tenant id is both the primary key and the foreign key.
/// </summary>
public sealed class TenantSettings : Entity<Guid>, ITenantScoped
{
    private TenantSettings() { }

    private TenantSettings(Guid tenantId, string timeZoneId, int bookingWindowDays) : base(tenantId)
    {
        TenantId = tenantId;
        TimeZoneId = timeZoneId;
        BookingWindowDays = bookingWindowDays;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>IANA time zone id (e.g. <c>Europe/Warsaw</c>). Defaults to <c>UTC</c>.</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    /// <summary>Contact email printed on public booking pages and confirmation emails.</summary>
    public string? ContactEmail { get; private set; }

    /// <summary>Primary brand colour — used in emails and the public booking UI.</summary>
    public string? BrandingPrimaryColor { get; private set; }

    /// <summary>Public URL to the tenant logo. Rendered on the booking page.</summary>
    public string? BrandingLogoUrl { get; private set; }

    /// <summary>How many days ahead the public booking flow will expose slots. Clamped to 1–365.</summary>
    public int BookingWindowDays { get; private set; } = 30;

    /// <summary>
    /// When <c>true</c> the worker's <c>NoShowMarker</c> job will automatically flip
    /// <see cref="Bookings.BookingStatus.Confirmed"/> bookings into
    /// <see cref="Bookings.BookingStatus.NoShow"/> after <see cref="NoShowGracePeriodMinutes"/>
    /// elapse past the booking end time. Opt-in — defaults to <c>false</c>.
    /// </summary>
    public bool NoShowAutoMarkEnabled { get; private set; }

    /// <summary>
    /// Grace period (minutes) added to <c>Booking.EndUtc</c> before a missed appointment
    /// is eligible for auto-no-show. Clamped to 0–240. Defaults to 15.
    /// </summary>
    public int NoShowGracePeriodMinutes { get; private set; } = 15;

    /// <summary>Last-update timestamp in UTC. Stamped by the handler.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Creates default settings for a freshly-registered tenant.</summary>
    public static TenantSettings CreateDefault(Guid tenantId) => new(tenantId, timeZoneId: "UTC", bookingWindowDays: 30);

    /// <summary>
    /// Replaces every field with the provided values. Each call stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public Result Update(
        string timeZoneId,
        int bookingWindowDays,
        string? contactEmail,
        string? brandingPrimaryColor,
        string? brandingLogoUrl,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Result.Failure(Error.Validation("TenantSettings.TimeZoneRequired", "Time zone id is required."));
        }
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return Result.Failure(Error.Validation("TenantSettings.TimeZoneUnknown", $"Time zone '{timeZoneId}' is not recognised by the system."));
        }
        if (bookingWindowDays is < 1 or > 365)
        {
            return Result.Failure(Error.Validation("TenantSettings.BookingWindowOutOfRange", "Booking window must be between 1 and 365 days."));
        }

        TimeZoneId = timeZoneId;
        BookingWindowDays = bookingWindowDays;
        ContactEmail = contactEmail;
        BrandingPrimaryColor = brandingPrimaryColor;
        BrandingLogoUrl = brandingLogoUrl;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Updates the auto-no-show policy. Grace is clamped; disabling the feature
    /// keeps the prior grace value for easy toggling.
    /// </summary>
    public Result SetNoShowPolicy(bool enabled, int graceMinutes, DateTimeOffset now)
    {
        if (graceMinutes is < 0 or > 240)
        {
            return Result.Failure(Error.Validation(
                "TenantSettings.NoShowGraceOutOfRange",
                "No-show grace period must be between 0 and 240 minutes."));
        }

        NoShowAutoMarkEnabled = enabled;
        NoShowGracePeriodMinutes = graceMinutes;
        UpdatedAt = now;
        return Result.Success();
    }
}
