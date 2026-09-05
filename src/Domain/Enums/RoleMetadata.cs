namespace Domain.Enums;

/// <summary>One role as the app presents it: what to call it and what colour it wears.</summary>
public sealed record RoleDefinition(
    Role Role,
    string Name,
    string Label,
    string PluralLabel,
    string Color,
    int Order);

/// <summary>
/// The catalogue of squad roles.
///
/// The seven built-ins below are the defaults and the values the <see cref="Role"/> enum
/// names. An administrator can add more from Settings → Roles; those are stored in the
/// database with values from <see cref="FirstCustomValue"/> upward, and
/// <see cref="Configure"/> replaces this registry with the full set at startup and after
/// every change.
///
/// Holding it here, rather than injecting a service into everything, keeps the twenty-odd
/// existing call sites — including <c>SquadComposition</c>, which is pure domain — working
/// unchanged. The trade-off is a process-wide registry: a second API instance does not
/// learn about a new role until it refreshes. That is acceptable for a single-instance
/// deployment and noted in the docs; the metadata endpoint always reads the database, so
/// the dropdown is never stale even when a label briefly falls back.
///
/// Colours must stay in step with web/src/styles/_tokens.scss for the built-ins — the
/// server renders exports from these same values.
/// </summary>
public static class RoleMetadata
{
    /// <summary>Custom roles start well above the built-ins, so the two never collide.</summary>
    public const int FirstCustomValue = 100;

    private static readonly IReadOnlyList<RoleDefinition> BuiltIns =
    [
        new(Role.ProductOwner,    nameof(Role.ProductOwner),    "Product Owner",    "Product Owners",    "#2DD4BF", 0),
        new(Role.TechLead,        nameof(Role.TechLead),        "Tech Lead",        "Tech Leads",        "#A78BFA", 1),
        new(Role.Developer,       nameof(Role.Developer),       "Developer",        "Developers",        "#6366F1", 2),
        new(Role.QaEngineer,      nameof(Role.QaEngineer),      "QA Engineer",      "QA Engineers",      "#F59E0B", 3),
        new(Role.UxDesigner,      nameof(Role.UxDesigner),      "UI/UX Designer",   "UI/UX Designers",   "#EC4899", 4),
        new(Role.BusinessAnalyst, nameof(Role.BusinessAnalyst), "Business Analyst", "Business Analysts", "#38BDF8", 5),
        new(Role.DevOps,          nameof(Role.DevOps),          "DevOps",           "DevOps",            "#10B981", 6)
    ];

    /// <summary>
    /// Swapped atomically as a whole snapshot, so a reader never sees a half-updated
    /// catalogue and no reader has to take a lock.
    /// </summary>
    private static volatile Snapshot _current = Snapshot.From(BuiltIns);

    /// <summary>The built-in seven, used to seed the database on first migration.</summary>
    public static IReadOnlyList<RoleDefinition> Defaults => BuiltIns;

    /// <summary>Roles in display order — the order of the composition bar and the legend.</summary>
    public static IReadOnlyList<Role> DisplayOrder => _current.Order;

    public static IReadOnlyList<RoleDefinition> All => _current.Definitions;

    /// <summary>
    /// Replaces the catalogue. Called once at startup and again whenever an admin changes
    /// the roles, so rendering agrees with the database.
    /// </summary>
    public static void Configure(IEnumerable<RoleDefinition> definitions)
    {
        var ordered = definitions
            .OrderBy(d => d.Order)
            .ThenBy(d => (int)d.Role)
            .ToList();

        // Refusing an empty catalogue is deliberate: it would blank every avatar and
        // legend in the app, and the cause would be invisible.
        _current = ordered.Count == 0 ? Snapshot.From(BuiltIns) : Snapshot.From(ordered);
    }

    /// <summary>Restores the built-in seven. For tests, so one does not leak into the next.</summary>
    public static void Reset() => _current = Snapshot.From(BuiltIns);

    /// <summary>
    /// Never throws. A role that is not in the catalogue — one deleted straight from the
    /// database, or a stale registry on another instance — renders as a neutral placeholder
    /// rather than taking a page down.
    /// </summary>
    public static RoleDefinition Get(Role role) =>
        _current.Map.TryGetValue(role, out var found) ? found : Unknown(role);

    public static string Label(Role role) => Get(role).Label;

    public static string Color(Role role) => Get(role).Color;

    /// <summary>Singular for a count of 1, plural otherwise ("1 QA Engineer", "2 Developers").</summary>
    public static string CountLabel(Role role, int count) =>
        count == 1 ? Get(role).Label : Get(role).PluralLabel;

    public static bool IsKnown(Role role) => _current.Map.ContainsKey(role);

    private static RoleDefinition Unknown(Role role) =>
        new(role, $"Role{(int)role}", $"Role {(int)role}", $"Role {(int)role}", "#8595A9", int.MaxValue);

    /// <summary>An immutable view of the catalogue, so readers need no synchronisation.</summary>
    private sealed record Snapshot(
        IReadOnlyList<RoleDefinition> Definitions,
        IReadOnlyDictionary<Role, RoleDefinition> Map,
        IReadOnlyList<Role> Order)
    {
        public static Snapshot From(IReadOnlyList<RoleDefinition> definitions) => new(
            definitions,
            definitions.ToDictionary(d => d.Role),
            definitions.Select(d => d.Role).ToList());
    }
}
