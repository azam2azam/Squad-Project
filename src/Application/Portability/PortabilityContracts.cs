using Domain.Enums;

namespace Application.Portability;

/// <summary>
/// The shape written by "export" and accepted by "import" — the production equivalent of
/// the prototype's Save/Load. Versioned so a future format change can be detected rather
/// than silently misread.
/// </summary>
public sealed record BoardExportFile(
    int Version,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedPerson> People,
    IReadOnlyList<ExportedBoard> Boards)
{
    public const int CurrentVersion = 1;
}

public sealed record ExportedPerson(
    Guid Id,
    string FullName,
    Role DefaultRole,
    string? DefaultDetail,
    string? Email,
    string? AvatarColorOverride,
    bool IsActive);

public sealed record ExportedBoard(
    Guid Id,
    string Title,
    string Product,
    string SquadName,
    string? Sprint,
    BoardStatus Status,
    int ProgressPercent,
    string? BlockerNote,
    double? Velocity,
    DateOnly? TargetDate,
    string? JiraProjectKey,
    string? JiraBoardId,
    int OrderIndex,
    IReadOnlyList<ExportedMember> Members,
    RiskLevel RiskLevel = RiskLevel.None,
    string? RiskNote = null);

public sealed record ExportedMember(
    Guid PersonId,
    Role Role,
    string? Detail,
    int? AllocationPercent,
    int OrderIndex);

/// <summary>What an import actually did, so the caller can report it rather than guess.</summary>
public sealed record ImportResult(
    int PeopleCreated,
    int PeopleUpdated,
    int BoardsCreated,
    int BoardsUpdated,
    int MembersLinked,
    IReadOnlyList<string> Warnings);
