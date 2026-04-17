using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Staff;

/// <summary>
/// A person in a tenant who performs bookable services (barber, dentist, coach, …).
/// Distinct from <c>ApplicationUser</c>: a Staff member may exist without login access.
/// Soft-deleted via <see cref="IsActive"/> — appointments keep their FK to inactive staff.
/// </summary>
public sealed class StaffMember : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Maximum display-name length.</summary>
    public const int MaxDisplayNameLength = 200;

    /// <summary>Maximum title length.</summary>
    public const int MaxTitleLength = 200;

    private StaffMember() { }

    private StaffMember(Guid id, Guid tenantId, string displayName, string? title, string? email, DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        DisplayName = displayName;
        Title = title;
        Email = email;
        CreatedAt = createdAt;
        IsActive = true;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Display name shown on the public booking page.</summary>
    public string DisplayName { get; private set; } = default!;

    /// <summary>Optional job title (e.g. "Senior Barber").</summary>
    public string? Title { get; private set; }

    /// <summary>Optional contact email (notifications, admin communication).</summary>
    public string? Email { get; private set; }

    /// <summary>Optional public avatar URL (filled in Phase-later when IBlobStorage is ready).</summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last-update timestamp.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Soft-delete flag.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Factory. Validates and returns the aggregate wrapped in a <see cref="Result{TValue}"/>.</summary>
    public static Result<StaffMember> Create(Guid id, Guid tenantId, string displayName, string? title, string? email, DateTimeOffset createdAt)
    {
        var nameResult = ValidateDisplayName(displayName);
        if (nameResult.IsFailure) return Result.Failure<StaffMember>(nameResult.Error);
        var titleResult = ValidateTitle(title);
        if (titleResult.IsFailure) return Result.Failure<StaffMember>(titleResult.Error);

        return new StaffMember(id, tenantId, displayName.Trim(), string.IsNullOrWhiteSpace(title) ? null : title.Trim(), string.IsNullOrWhiteSpace(email) ? null : email.Trim(), createdAt);
    }

    /// <summary>Updates the mutable fields. Stamps <see cref="UpdatedAt"/>.</summary>
    public Result Update(string displayName, string? title, string? email, DateTimeOffset now)
    {
        var nameResult = ValidateDisplayName(displayName);
        if (nameResult.IsFailure) return nameResult;
        var titleResult = ValidateTitle(title);
        if (titleResult.IsFailure) return titleResult;

        DisplayName = displayName.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Sets the avatar URL (called from Phase-later avatar-upload flow).</summary>
    public void SetAvatarUrl(string? url, DateTimeOffset now)
    {
        AvatarUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        UpdatedAt = now;
    }

    /// <summary>Soft-delete. Idempotent.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
    }

    /// <summary>Reactivate. Idempotent.</summary>
    public void Activate(DateTimeOffset now)
    {
        if (IsActive) return;
        IsActive = true;
        UpdatedAt = now;
    }

    private static Result ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result.Failure(Error.Validation("Staff.DisplayNameRequired", "Display name is required."));
        }
        if (displayName.Length > MaxDisplayNameLength)
        {
            return Result.Failure(Error.Validation("Staff.DisplayNameTooLong", $"Display name must be {MaxDisplayNameLength} characters or fewer."));
        }
        return Result.Success();
    }

    private static Result ValidateTitle(string? title)
    {
        if (title is not null && title.Length > MaxTitleLength)
        {
            return Result.Failure(Error.Validation("Staff.TitleTooLong", $"Title must be {MaxTitleLength} characters or fewer."));
        }
        return Result.Success();
    }
}
