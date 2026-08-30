namespace Domain.Enums;

/// <summary>
/// Canonical display label, plural label and hex colour for each <see cref="Role"/>.
/// Colours are the prototype design tokens and must stay in step with
/// web/src/styles/_tokens.scss — the server renders exports from these same values.
/// </summary>
public static class RoleMetadata
{
    private static readonly IReadOnlyDictionary<Role, RoleInfo> Map = new Dictionary<Role, RoleInfo>
    {
        [Role.ProductOwner]    = new("Product Owner",    "Product Owners",    "#2DD4BF"),
        [Role.TechLead]        = new("Tech Lead",        "Tech Leads",        "#A78BFA"),
        [Role.Developer]       = new("Developer",        "Developers",        "#6366F1"),
        [Role.QaEngineer]      = new("QA Engineer",      "QA Engineers",      "#F59E0B"),
        [Role.UxDesigner]      = new("UI/UX Designer",   "UI/UX Designers",   "#EC4899"),
        [Role.BusinessAnalyst] = new("Business Analyst", "Business Analysts", "#38BDF8"),
        [Role.DevOps]          = new("DevOps",           "DevOps",            "#10B981")
    };

    /// <summary>Roles in the canonical display order used by the composition bar and legend.</summary>
    public static IReadOnlyList<Role> DisplayOrder { get; } =
    [
        Role.ProductOwner, Role.TechLead, Role.Developer, Role.QaEngineer,
        Role.UxDesigner, Role.BusinessAnalyst, Role.DevOps
    ];

    public static RoleInfo Get(Role role) => Map[role];

    public static string Label(Role role) => Map[role].Label;

    public static string Color(Role role) => Map[role].Color;

    /// <summary>Singular label for a count of 1, plural otherwise ("1 QA Engineer", "2 Developers").</summary>
    public static string CountLabel(Role role, int count) =>
        count == 1 ? Map[role].Label : Map[role].PluralLabel;
}

public readonly record struct RoleInfo(string Label, string PluralLabel, string Color);
