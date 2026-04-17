using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.ValueObjects;

namespace BookSlot.Domain.Services;

/// <summary>
/// A bookable service offered by a tenant. Configurable duration, buffers, price,
/// and a per-tenant URL slug. Soft-deletion via <see cref="IsActive"/> preserves
/// historical booking references.
/// </summary>
public sealed class ServiceType : AggregateRoot<Guid>, ITenantScoped
{
    /// <summary>Minimum bookable duration, in minutes.</summary>
    public const int MinDurationMinutes = 5;

    /// <summary>Maximum bookable duration, in minutes (8 hours).</summary>
    public const int MaxDurationMinutes = 480;

    /// <summary>Maximum buffer minutes before/after an appointment.</summary>
    public const int MaxBufferMinutes = 240;

    /// <summary>Maximum description length.</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>Maximum display-name length.</summary>
    public const int MaxNameLength = 200;

    private ServiceType() { }

    private ServiceType(
        Guid id,
        Guid tenantId,
        string name,
        string slug,
        int durationMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        decimal price,
        string currency,
        string? description,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Slug = slug;
        DurationMinutes = durationMinutes;
        BufferBeforeMinutes = bufferBeforeMinutes;
        BufferAfterMinutes = bufferAfterMinutes;
        Price = price;
        Currency = currency;
        Description = description;
        CreatedAt = createdAt;
        IsActive = true;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Human-readable name displayed on the public booking page.</summary>
    public string Name { get; private set; } = default!;

    /// <summary>URL-safe slug, unique within the tenant. Immutable after creation.</summary>
    public string Slug { get; private set; } = default!;

    /// <summary>Core appointment length in minutes.</summary>
    public int DurationMinutes { get; private set; }

    /// <summary>Non-bookable buffer before the appointment (prep time).</summary>
    public int BufferBeforeMinutes { get; private set; }

    /// <summary>Non-bookable buffer after the appointment (cleanup / admin).</summary>
    public int BufferAfterMinutes { get; private set; }

    /// <summary>List price in <see cref="Currency"/>.</summary>
    public decimal Price { get; private set; }

    /// <summary>ISO 4217 currency code (3 uppercase letters).</summary>
    public string Currency { get; private set; } = default!;

    /// <summary>Optional long-form description shown on the public page.</summary>
    public string? Description { get; private set; }

    /// <summary>Creation timestamp in UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last-update timestamp in UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Soft-delete flag. Historical bookings keep referencing inactive services.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Optional JSON document describing additional fields collected from guests
    /// on the public booking page. When null no custom fields are shown.
    /// </summary>
    public string? FormSchemaJson { get; private set; }

    /// <summary>
    /// Replaces (or clears) the custom form schema attached to this service.
    /// Pass null or whitespace to remove the schema entirely.
    /// </summary>
    public Result SetFormSchema(string? schemaJson, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            FormSchemaJson = null;
            UpdatedAt = now;
            return Result.Success();
        }

        var parsed = BookingFormSchema.Parse(schemaJson);
        if (parsed.IsFailure) return Result.Failure(parsed.Error);

        FormSchemaJson = schemaJson;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Factory. Validates every field, returns <see cref="Result{TValue}"/> with the aggregate.</summary>
    public static Result<ServiceType> Create(
        Guid id,
        Guid tenantId,
        string name,
        Slug slug,
        int durationMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        decimal price,
        string currency,
        string? description,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return Result.Failure<ServiceType>(nameResult.Error);
        }

        var durationResult = ValidateDuration(durationMinutes);
        if (durationResult.IsFailure)
        {
            return Result.Failure<ServiceType>(durationResult.Error);
        }

        var buffersResult = ValidateBuffers(bufferBeforeMinutes, bufferAfterMinutes);
        if (buffersResult.IsFailure)
        {
            return Result.Failure<ServiceType>(buffersResult.Error);
        }

        var priceResult = ValidatePrice(price);
        if (priceResult.IsFailure)
        {
            return Result.Failure<ServiceType>(priceResult.Error);
        }

        var currencyResult = ValidateCurrency(currency);
        if (currencyResult.IsFailure)
        {
            return Result.Failure<ServiceType>(currencyResult.Error);
        }

        var descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
        {
            return Result.Failure<ServiceType>(descriptionResult.Error);
        }

        return new ServiceType(
            id,
            tenantId,
            name.Trim(),
            slug.Value,
            durationMinutes,
            bufferBeforeMinutes,
            bufferAfterMinutes,
            price,
            currency.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            createdAt);
    }

    /// <summary>Applies a full-replacement update (slug immutable). Stamps <see cref="UpdatedAt"/>.</summary>
    public Result Update(
        string name,
        int durationMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        decimal price,
        string currency,
        string? description,
        DateTimeOffset now)
    {
        var nameResult = ValidateName(name);
        if (nameResult.IsFailure) return nameResult;

        var durationResult = ValidateDuration(durationMinutes);
        if (durationResult.IsFailure) return durationResult;

        var buffersResult = ValidateBuffers(bufferBeforeMinutes, bufferAfterMinutes);
        if (buffersResult.IsFailure) return buffersResult;

        var priceResult = ValidatePrice(price);
        if (priceResult.IsFailure) return priceResult;

        var currencyResult = ValidateCurrency(currency);
        if (currencyResult.IsFailure) return currencyResult;

        var descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure) return descriptionResult;

        Name = name.Trim();
        DurationMinutes = durationMinutes;
        BufferBeforeMinutes = bufferBeforeMinutes;
        BufferAfterMinutes = bufferAfterMinutes;
        Price = price;
        Currency = currency.ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Soft-delete: flip <see cref="IsActive"/> off. Idempotent.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        UpdatedAt = now;
    }

    /// <summary>Reactivates a previously deactivated service. Idempotent.</summary>
    public void Activate(DateTimeOffset now)
    {
        if (IsActive) return;
        IsActive = true;
        UpdatedAt = now;
    }

    private static Result ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("ServiceType.NameRequired", "Name is required."));
        }
        if (name.Length > MaxNameLength)
        {
            return Result.Failure(Error.Validation("ServiceType.NameTooLong", $"Name must be {MaxNameLength} characters or fewer."));
        }
        return Result.Success();
    }

    private static Result ValidateDuration(int durationMinutes)
    {
        if (durationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            return Result.Failure(Error.Validation(
                "ServiceType.DurationOutOfRange",
                $"Duration must be between {MinDurationMinutes} and {MaxDurationMinutes} minutes."));
        }
        return Result.Success();
    }

    private static Result ValidateBuffers(int before, int after)
    {
        if (before < 0 || before > MaxBufferMinutes || after < 0 || after > MaxBufferMinutes)
        {
            return Result.Failure(Error.Validation(
                "ServiceType.BufferOutOfRange",
                $"Buffers must be between 0 and {MaxBufferMinutes} minutes."));
        }
        return Result.Success();
    }

    private static Result ValidatePrice(decimal price)
    {
        if (price < 0m)
        {
            return Result.Failure(Error.Validation("ServiceType.PriceNegative", "Price cannot be negative."));
        }
        return Result.Success();
    }

    private static Result ValidateCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            return Result.Failure(Error.Validation("ServiceType.CurrencyInvalid", "Currency must be a 3-letter ISO 4217 code."));
        }
        foreach (var c in currency)
        {
            if (!char.IsLetter(c))
            {
                return Result.Failure(Error.Validation("ServiceType.CurrencyInvalid", "Currency must be a 3-letter ISO 4217 code."));
            }
        }
        return Result.Success();
    }

    private static Result ValidateDescription(string? description)
    {
        if (description is not null && description.Length > MaxDescriptionLength)
        {
            return Result.Failure(Error.Validation(
                "ServiceType.DescriptionTooLong",
                $"Description must be {MaxDescriptionLength} characters or fewer."));
        }
        return Result.Success();
    }
}
