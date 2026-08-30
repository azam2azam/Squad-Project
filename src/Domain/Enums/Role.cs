namespace Domain.Enums;

/// <summary>
/// Squad roles. Values are stable and persisted; do not renumber.
/// Display labels and colours live in <see cref="RoleMetadata"/>.
/// </summary>
public enum Role
{
    ProductOwner = 0,
    TechLead = 1,
    Developer = 2,
    QaEngineer = 3,
    UxDesigner = 4,
    BusinessAnalyst = 5,
    DevOps = 6
}
