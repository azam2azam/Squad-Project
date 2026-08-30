namespace Domain.Enums;

/// <summary>
/// Delivery risk, tracked separately from <see cref="BoardStatus"/>.
///
/// Status says where the work IS; risk says how likely it is to go wrong. A board can
/// be On Track and High risk at the same time — a dependency about to slip, a key person
/// leaving — and collapsing the two would hide exactly the boards a delivery lead needs
/// to look at.
///
/// Values are stable and persisted; do not renumber.
/// </summary>
public enum RiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Labels and colours for risk. These map onto the reserved status palette
/// (good / warning / serious / critical) rather than introducing new hues, so risk
/// never competes with the role colours for meaning.
/// </summary>
public static class RiskLevelMetadata
{
    private static readonly IReadOnlyDictionary<RiskLevel, RiskInfo> Map =
        new Dictionary<RiskLevel, RiskInfo>
        {
            [RiskLevel.None]     = new("No risk",  "#8595A9"),
            [RiskLevel.Low]      = new("Low",      "#34D399"),
            [RiskLevel.Medium]   = new("Medium",   "#FBBF24"),
            [RiskLevel.High]     = new("High",     "#FB923C"),
            [RiskLevel.Critical] = new("Critical", "#F87171")
        };

    public static IReadOnlyList<RiskLevel> DisplayOrder { get; } =
    [
        RiskLevel.Critical, RiskLevel.High, RiskLevel.Medium, RiskLevel.Low, RiskLevel.None
    ];

    public static string Label(RiskLevel level) => Map[level].Label;

    public static string Color(RiskLevel level) => Map[level].Color;

    /// <summary>Medium and above is what belongs on a delivery lead's risk register.</summary>
    public static bool IsNotable(RiskLevel level) => level >= RiskLevel.Medium;
}

public readonly record struct RiskInfo(string Label, string Color);
