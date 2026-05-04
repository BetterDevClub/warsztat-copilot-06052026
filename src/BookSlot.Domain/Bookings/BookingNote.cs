using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;

namespace BookSlot.Domain.Bookings;

/// <summary>
/// An internal staff note attached to a booking. Not visible to guests.
/// </summary>
public sealed class BookingNote : Entity<Guid>, ITenantScoped
{
    /// <summary>Maximum length of a note's content.</summary>
    public const int MaxContentLength = 2000;

    /// <summary>Maximum number of notes allowed per booking.</summary>
    public const int MaxNotesPerBooking = 50;

    // EF Core required parameterless constructor.
    private BookingNote() { }

    private BookingNote(
        Guid id,
        Guid tenantId,
        Guid bookingId,
        Guid authorId,
        string content,
        DateTimeOffset createdAt) : base(id)
    {
        TenantId = tenantId;
        BookingId = bookingId;
        AuthorId = authorId;
        Content = content;
        CreatedAt = createdAt;
    }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The booking this note belongs to.</summary>
    public Guid BookingId { get; private set; }

    /// <summary>The staff user who authored this note.</summary>
    public Guid AuthorId { get; private set; }

    /// <summary>Note body text.</summary>
    public string Content { get; private set; } = default!;

    /// <summary>UTC timestamp when the note was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a new <see cref="BookingNote"/> after validating all inputs and
    /// enforcing the per-booking note cap.
    /// </summary>
    public static Result<BookingNote> Create(
        Guid id,
        Guid tenantId,
        Guid bookingId,
        Guid authorId,
        string content,
        DateTimeOffset createdAt,
        int existingNotesCount)
    {
        if (id == Guid.Empty)
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.IdEmpty", "Note id must not be empty."));

        if (tenantId == Guid.Empty)
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.TenantIdEmpty", "Tenant id must not be empty."));

        if (bookingId == Guid.Empty)
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.BookingIdEmpty", "Booking id must not be empty."));

        if (authorId == Guid.Empty)
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.AuthorIdEmpty", "Author id must not be empty."));

        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.ContentEmpty", "Note content is required."));

        if (content.Length > MaxContentLength)
            return Result.Failure<BookingNote>(Error.Validation("BookingNote.ContentTooLong",
                $"Note content must be {MaxContentLength} characters or fewer."));

        if (existingNotesCount >= MaxNotesPerBooking)
            return Result.Failure<BookingNote>(BookingErrors.NotesTooMany);

        return new BookingNote(id, tenantId, bookingId, authorId, content.Trim(), createdAt);
    }
}
