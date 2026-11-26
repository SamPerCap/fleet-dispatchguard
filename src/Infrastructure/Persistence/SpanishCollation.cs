using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>
/// Ignores case and tells accents apart, in every comparison, order and unique index of a text (SPEC 0092, ADR 0024). Each column
/// carries it, because PostgreSQL does not admit a collation like this as the one of a whole database.
/// </summary>
internal static class SpanishCollation
{
    public const string Name = "spanish_ci_as";

    /// <summary>The second level of the Unicode comparison: letters and accents, without the third, which is the case.</summary>
    private const string Locale = "es-u-ks-level2";

    public static void AddTo(ModelBuilder modelBuilder) =>
        modelBuilder.HasCollation(Name, locale: Locale, provider: "icu", deterministic: false);
}
