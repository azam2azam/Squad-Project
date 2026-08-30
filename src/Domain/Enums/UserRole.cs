namespace Domain.Enums;

/// <summary>
/// Access level (spec section 8). Values are stable and persisted; do not renumber.
/// Ordered by privilege so comparisons like <c>role &gt;= UserRole.ProductOwner</c> read
/// the way they mean.
/// </summary>
public enum UserRole
{
    /// <summary>Read-only: view, present and export. Cannot write anything.</summary>
    Viewer = 0,

    /// <summary>Full control of boards they own, plus read access to everyone else's.</summary>
    ProductOwner = 1,

    /// <summary>Everything: all boards, the roster, imports and exports.</summary>
    Admin = 2
}

public static class UserRoleNames
{
    public const string Viewer = nameof(UserRole.Viewer);
    public const string ProductOwner = nameof(UserRole.ProductOwner);
    public const string Admin = nameof(UserRole.Admin);
}
