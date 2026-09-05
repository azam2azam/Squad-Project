using Domain.Enums;

namespace Domain.ValueObjects;

/// <summary>
/// Per-role headcount for a squad, plus the derived legend text and bar segments.
/// Computed server-side so exports and the API agree with the client without
/// the client having to be trusted.
/// </summary>
public sealed record SquadComposition
{
    private SquadComposition(IReadOnlyList<CompositionSegment> segments, int total)
    {
        Segments = segments;
        Total = total;
    }

    /// <summary>Segments in canonical role order, only for roles with a non-zero count.</summary>
    public IReadOnlyList<CompositionSegment> Segments { get; }

    public int Total { get; }

    /// <summary>Legend text, e.g. "2 Developers · 1 QA Engineer · 1 Product Owner".</summary>
    public string LegendText => Segments.Count == 0
        ? "No members yet"
        : string.Join(" · ", Segments.Select(s => $"{s.Count} {RoleMetadata.CountLabel(s.Role, s.Count)}"));

    public int CountOf(Role role) => Segments.FirstOrDefault(s => s.Role == role)?.Count ?? 0;

    /// <summary>
    /// Builds the composition from a set of assigned roles. Percentages are
    /// rounded to two decimals and always sum to 100 for a non-empty squad.
    /// </summary>
    public static SquadComposition From(IEnumerable<Role> roles)
    {
        var counts = new Dictionary<Role, int>();
        foreach (var role in roles)
        {
            counts.TryGetValue(role, out var existing);
            counts[role] = existing + 1;
        }

        var total = counts.Values.Sum();
        if (total == 0)
        {
            return new SquadComposition([], 0);
        }

        // Catalogue order first, then anything counted that the catalogue does not know
        // about — a custom role added on another instance must still appear on the bar
        // rather than silently vanishing and leaving the percentages short of 100.
        var ordered = RoleMetadata.DisplayOrder
            .Where(counts.ContainsKey)
            .Concat(counts.Keys.Where(r => !RoleMetadata.IsKnown(r)).OrderBy(r => (int)r));

        var segments = ordered
            .Select(role => new CompositionSegment(
                role,
                RoleMetadata.Label(role),
                RoleMetadata.Get(role).PluralLabel,
                RoleMetadata.Color(role),
                counts[role],
                Math.Round(counts[role] * 100d / total, 2)))
            .ToList();

        // Push any rounding drift onto the largest segment so the bar always fills exactly.
        var drift = 100d - segments.Sum(s => s.Percent);
        if (Math.Abs(drift) > 0.0001)
        {
            var widest = segments.OrderByDescending(s => s.Percent).First();
            var index = segments.IndexOf(widest);
            segments[index] = widest with { Percent = Math.Round(widest.Percent + drift, 2) };
        }

        return new SquadComposition(segments, total);
    }

    public static SquadComposition Empty { get; } = new([], 0);
}

/// <summary>One role's slice of the composition bar.</summary>
public sealed record CompositionSegment(
    Role Role, string Label, string PluralLabel, string Color, int Count, double Percent);
