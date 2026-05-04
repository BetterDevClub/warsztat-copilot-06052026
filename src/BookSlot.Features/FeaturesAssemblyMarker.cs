using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BookSlot.UnitTests")]

namespace BookSlot.Features;

/// <summary>
/// Marker type used to locate this assembly via reflection
/// (e.g. endpoint / validator / handler auto-registration).
/// </summary>
public static class FeaturesAssemblyMarker
{
    /// <summary>The <see cref="global::System.Reflection.Assembly"/> for <c>BookSlot.Features</c>.</summary>
    public static readonly global::System.Reflection.Assembly Assembly = typeof(FeaturesAssemblyMarker).Assembly;
}
