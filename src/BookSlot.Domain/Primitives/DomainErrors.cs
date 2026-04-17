namespace BookSlot.Domain.Primitives;

/// <summary>Shared error catalogue for domain primitives.</summary>
public static class DomainErrors
{
    /// <summary>Email-related errors.</summary>
    public static class EmailErrors
    {
        /// <summary>Email was null or whitespace.</summary>
        public static readonly Error Empty = Error.Validation("Email.Empty", "Email must not be empty.");

        /// <summary>Email exceeded 254 chars (RFC 5321).</summary>
        public static readonly Error TooLong = Error.Validation("Email.TooLong", "Email must be at most 254 characters.");

        /// <summary>Email format invalid.</summary>
        public static readonly Error Invalid = Error.Validation("Email.Invalid", "Email format is invalid.");
    }

    /// <summary>Phone-related errors.</summary>
    public static class PhoneErrors
    {
        /// <summary>Phone was null or whitespace.</summary>
        public static readonly Error Empty = Error.Validation("Phone.Empty", "Phone must not be empty.");

        /// <summary>Phone not in E.164 format.</summary>
        public static readonly Error Invalid = Error.Validation("Phone.Invalid", "Phone must be in E.164 format (e.g. +15551234567).");
    }

    /// <summary>Slug-related errors.</summary>
    public static class SlugErrors
    {
        /// <summary>Slug was null or whitespace.</summary>
        public static readonly Error Empty = Error.Validation("Slug.Empty", "Slug must not be empty.");

        /// <summary>Slug too long.</summary>
        public static readonly Error TooLong = Error.Validation("Slug.TooLong", "Slug must be at most 64 characters.");

        /// <summary>Slug too short.</summary>
        public static readonly Error TooShort = Error.Validation("Slug.TooShort", "Slug must be at least 3 characters.");

        /// <summary>Slug contains invalid characters.</summary>
        public static readonly Error Invalid = Error.Validation(
            "Slug.Invalid",
            "Slug must be lowercase, start with a letter, and contain only letters, digits and hyphens.");

        /// <summary>Slug is a reserved word.</summary>
        public static readonly Error Reserved = Error.Validation("Slug.Reserved", "Slug is reserved and cannot be used.");
    }
}
