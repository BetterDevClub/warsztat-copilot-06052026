using System.Diagnostics;

namespace BookSlot.Infrastructure.Observability;

/// <summary>
/// Single ActivitySource shared by domain/feature code that wants to emit custom
/// spans. Keeping the name centralised avoids drift across slices and makes the
/// OTel pipeline registration one-liner.
/// </summary>
public static class BookSlotActivitySource
{
    public const string Name = "BookSlot";

    public static readonly ActivitySource Instance = new(Name, "1.0.0");
}
