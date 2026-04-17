using System.Globalization;
using System.Text.Json;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Services;

/// <summary>Supported dynamic booking form field types.</summary>
public enum BookingFormFieldType
{
    /// <summary>Single-line text input.</summary>
    Text = 1,

    /// <summary>Multi-line text area.</summary>
    TextArea = 2,

    /// <summary>Numeric input (stored as decimal).</summary>
    Number = 3,

    /// <summary>Boolean checkbox.</summary>
    Checkbox = 4,

    /// <summary>Dropdown selection — value must be one of <see cref="BookingFormField.Options"/>.</summary>
    Select = 5,
}

/// <summary>Declarative description of a single custom booking form field.</summary>
public sealed record BookingFormField(
    string Key,
    string Label,
    BookingFormFieldType Type,
    bool Required,
    int? MinLength,
    int? MaxLength,
    decimal? Min,
    decimal? Max,
    IReadOnlyList<string>? Options);

/// <summary>
/// Per-service-type dynamic form schema. The raw JSON is stored as-is on the
/// <see cref="ServiceType"/>; this record is the parsed, validated view used
/// by both the admin API and the Blazor renderer (Phase 28).
/// </summary>
public sealed class BookingFormSchema
{
    /// <summary>Maximum allowed fields per schema.</summary>
    public const int MaxFields = 20;

    /// <summary>Maximum length of the stored JSON document.</summary>
    public const int MaxJsonLength = 16 * 1024;

    private BookingFormSchema(IReadOnlyList<BookingFormField> fields) => Fields = fields;

    /// <summary>The declared fields, in display order.</summary>
    public IReadOnlyList<BookingFormField> Fields { get; }

    /// <summary>Parses and validates a schema JSON document. Returns Failure for malformed input.</summary>
    public static Result<BookingFormSchema> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result.Failure<BookingFormSchema>(Error.Validation("BookingFormSchema.Empty", "Schema JSON must be non-empty."));

        if (json.Length > MaxJsonLength)
            return Result.Failure<BookingFormSchema>(Error.Validation("BookingFormSchema.TooLarge",
                $"Schema JSON must be {MaxJsonLength} characters or fewer."));

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return Result.Failure<BookingFormSchema>(Error.Validation("BookingFormSchema.InvalidJson", "Schema JSON is not well-formed.")); }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("fields", out var fieldsEl)
                || fieldsEl.ValueKind != JsonValueKind.Array)
                return Result.Failure<BookingFormSchema>(Error.Validation("BookingFormSchema.MissingFields", "Schema must have a 'fields' array."));

            var parsed = new List<BookingFormField>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in fieldsEl.EnumerateArray())
            {
                var fieldResult = ParseField(el, seenKeys);
                if (fieldResult.IsFailure) return Result.Failure<BookingFormSchema>(fieldResult.Error);
                parsed.Add(fieldResult.Value);
            }

            if (parsed.Count > MaxFields)
                return Result.Failure<BookingFormSchema>(Error.Validation("BookingFormSchema.TooManyFields",
                    $"Schema may declare at most {MaxFields} fields."));

            return new BookingFormSchema(parsed);
        }
    }

    /// <summary>
    /// Validates a dictionary of submitted values against the schema. Returns an error
    /// for the first failed field (validation stops on first error for simplicity).
    /// </summary>
    public Result Validate(IReadOnlyDictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var field in Fields)
        {
            var present = values.TryGetValue(field.Key, out var value)
                && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

            if (!present)
            {
                if (field.Required)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Required",
                        $"Field '{field.Label}' is required."));
                continue;
            }

            var fieldResult = ValidateValue(field, value);
            if (fieldResult.IsFailure) return fieldResult;
        }

        // Reject unknown keys so tampering is surfaced rather than silently swallowed.
        var known = new HashSet<string>(Fields.Select(f => f.Key), StringComparer.Ordinal);
        foreach (var key in values.Keys)
            if (!known.Contains(key))
                return Result.Failure(Error.Validation("CustomField.Unknown", $"Field '{key}' is not declared in the schema."));

        return Result.Success();
    }

    // -------------------------------------------------------------------------

    private static Result<BookingFormField> ParseField(JsonElement el, HashSet<string> seenKeys)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.FieldNotObject", "Each field must be a JSON object."));

        var key = GetString(el, "key");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.FieldKeyInvalid",
                "Each field requires a 'key' of 1–64 characters."));

        foreach (var c in key)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.FieldKeyCharset",
                    $"Field key '{key}' may only contain letters, digits, '_' or '-'."));

        if (!seenKeys.Add(key))
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.DuplicateKey",
                $"Duplicate field key '{key}'."));

        var label = GetString(el, "label") ?? key;
        if (label.Length > 200)
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.LabelTooLong",
                "Field label must be 200 characters or fewer."));

        var typeString = GetString(el, "type");
        if (!Enum.TryParse<BookingFormFieldType>(typeString, ignoreCase: true, out var type))
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.TypeInvalid",
                $"Field '{key}' has unknown type '{typeString}'."));

        var required = el.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True;
        var minLength = GetInt(el, "minLength");
        var maxLength = GetInt(el, "maxLength");
        var min = GetDecimal(el, "min");
        var max = GetDecimal(el, "max");

        List<string>? options = null;
        if (el.TryGetProperty("options", out var optionsEl) && optionsEl.ValueKind == JsonValueKind.Array)
        {
            options = new List<string>();
            foreach (var o in optionsEl.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.String)
                    return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.OptionInvalid",
                        $"Option of field '{key}' must be a string."));
                options.Add(o.GetString()!);
            }
        }

        if (type == BookingFormFieldType.Select && (options is null || options.Count == 0))
            return Result.Failure<BookingFormField>(Error.Validation("BookingFormSchema.SelectWithoutOptions",
                $"Select field '{key}' must declare at least one option."));

        return new BookingFormField(key, label, type, required, minLength, maxLength, min, max, options);
    }

    private static Result ValidateValue(BookingFormField field, JsonElement value)
    {
        switch (field.Type)
        {
            case BookingFormFieldType.Text:
            case BookingFormFieldType.TextArea:
                if (value.ValueKind != JsonValueKind.String)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Type",
                        $"Field '{field.Label}' must be a string."));
                var s = value.GetString() ?? string.Empty;
                if (field.MinLength is { } minL && s.Length < minL)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.MinLength",
                        $"Field '{field.Label}' must be at least {minL} characters."));
                if (field.MaxLength is { } maxL && s.Length > maxL)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.MaxLength",
                        $"Field '{field.Label}' must be {maxL} characters or fewer."));
                return Result.Success();

            case BookingFormFieldType.Number:
                if (value.ValueKind != JsonValueKind.Number)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Type",
                        $"Field '{field.Label}' must be a number."));
                var n = value.GetDecimal();
                if (field.Min is { } min && n < min)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Min",
                        $"Field '{field.Label}' must be >= {min.ToString(CultureInfo.InvariantCulture)}."));
                if (field.Max is { } max && n > max)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Max",
                        $"Field '{field.Label}' must be <= {max.ToString(CultureInfo.InvariantCulture)}."));
                return Result.Success();

            case BookingFormFieldType.Checkbox:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Type",
                        $"Field '{field.Label}' must be a boolean."));
                return Result.Success();

            case BookingFormFieldType.Select:
                if (value.ValueKind != JsonValueKind.String)
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.Type",
                        $"Field '{field.Label}' must be a string option."));
                var selected = value.GetString();
                if (field.Options is null || !field.Options.Contains(selected!, StringComparer.Ordinal))
                    return Result.Failure(Error.Validation($"CustomField.{field.Key}.InvalidOption",
                        $"Field '{field.Label}' must be one of: {string.Join(", ", field.Options ?? Array.Empty<string>())}."));
                return Result.Success();

            default:
                return Result.Failure(Error.Validation($"CustomField.{field.Key}.UnknownType",
                    $"Field '{field.Label}' has an unsupported type."));
        }
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static decimal? GetDecimal(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
}
