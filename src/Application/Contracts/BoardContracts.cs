using Domain.Entities;
using Domain.Enums;

namespace Application.Contracts;

/// <summary>Compact board shape for the portfolio grid and list endpoints.</summary>
public sealed record BoardSummaryDto(
    Guid Id,
    string Title,
    string Product,
    string SquadName,
    string? Sprint,
    BoardStatus Status,
    string StatusLabel,
    string StatusColor,
    int ProgressPercent,
    int MemberCount,
    string CompositionLegend,
    int OrderIndex,
    DateTimeOffset UpdatedAt)
{
    public static BoardSummaryDto From(Board board) => new(
        board.Id,
        board.Title,
        board.Product,
        board.SquadName,
        board.Sprint,
        board.Status,
        BoardStatusMetadata.Label(board.Status),
        BoardStatusMetadata.Color(board.Status),
        board.ProgressPercent,
        board.Members.Count,
        board.Composition.LegendText,
        board.OrderIndex,
        board.UpdatedAt);
}

/// <summary>Full board shape for the editor, present mode and exports.</summary>
public sealed record BoardDetailDto(
    Guid Id,
    string Title,
    string Product,
    string SquadName,
    string? Sprint,
    BoardStatus Status,
    string StatusLabel,
    string StatusColor,
    int ProgressPercent,
    string? BlockerNote,
    double? Velocity,
    DateOnly? TargetDate,
    string? JiraProjectKey,
    string? JiraBoardId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int OrderIndex,
    IReadOnlyList<SquadMemberDto> Members,
    CompositionDto Composition,
    IReadOnlyList<string> Warnings)
{
    public static BoardDetailDto From(Board board) => new(
        board.Id,
        board.Title,
        board.Product,
        board.SquadName,
        board.Sprint,
        board.Status,
        BoardStatusMetadata.Label(board.Status),
        BoardStatusMetadata.Color(board.Status),
        board.ProgressPercent,
        board.BlockerNote,
        board.Velocity,
        board.TargetDate,
        board.JiraProjectKey,
        board.JiraBoardId,
        board.CreatedBy,
        board.CreatedAt,
        board.UpdatedAt,
        board.OrderIndex,
        board.Members.OrderBy(m => m.OrderIndex).Select(SquadMemberDto.From).ToList(),
        CompositionDto.From(board),
        board.Warnings);
}

/// <summary>One avatar card on the slide.</summary>
public sealed record SquadMemberDto(
    Guid Id,
    Guid PersonId,
    string FullName,
    string Initials,
    Role Role,
    string RoleLabel,
    string RoleColor,
    string? Detail,
    int? AllocationPercent,
    int OrderIndex)
{
    public static SquadMemberDto From(SquadMember member) => new(
        member.Id,
        member.PersonId,
        member.Person.FullName,
        member.Person.Initials,
        member.Role,
        RoleMetadata.Label(member.Role),
        // A person-level override wins over the role colour when one is set.
        member.Person.AvatarColorOverride ?? RoleMetadata.Color(member.Role),
        member.Detail ?? member.Person.DefaultDetail,
        member.AllocationPercent,
        member.OrderIndex);
}

/// <summary>
/// Server-derived composition. Computed here as well as on the client so exports
/// and API consumers never depend on client-side arithmetic (spec section 5).
/// </summary>
public sealed record CompositionDto(
    int Total,
    string LegendText,
    IReadOnlyList<CompositionSegmentDto> Segments)
{
    public static CompositionDto From(Board board)
    {
        var composition = board.Composition;
        return new CompositionDto(
            composition.Total,
            composition.LegendText,
            composition.Segments
                .Select(s => new CompositionSegmentDto(
                    s.Role, s.Label, s.PluralLabel, s.Color, s.Count, s.Percent))
                .ToList());
    }
}

public sealed record CompositionSegmentDto(
    Role Role, string Label, string PluralLabel, string Color, int Count, double Percent);

/// <summary>Change-log line for the board history panel.</summary>
public sealed record BoardAuditEntryDto(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    string ChangedBy,
    DateTimeOffset ChangedAt,
    string Summary)
{
    public static BoardAuditEntryDto From(BoardAuditEntry entry) => new(
        entry.Id, entry.Field, entry.OldValue, entry.NewValue,
        entry.ChangedBy, entry.ChangedAt, entry.Summary);
}

/// <summary>Roster entry for the typeahead and roster manager.</summary>
public sealed record PersonDto(
    Guid Id,
    string FullName,
    string Initials,
    Role DefaultRole,
    string DefaultRoleLabel,
    string DefaultRoleColor,
    string? DefaultDetail,
    string? Email,
    string? AvatarColorOverride,
    bool IsActive)
{
    public static PersonDto From(Person person) => new(
        person.Id,
        person.FullName,
        person.Initials,
        person.DefaultRole,
        RoleMetadata.Label(person.DefaultRole),
        person.AvatarColorOverride ?? RoleMetadata.Color(person.DefaultRole),
        person.DefaultDetail,
        person.Email,
        person.AvatarColorOverride,
        person.IsActive);
}

/// <summary>Static role and status reference data, so the client never hardcodes tokens.</summary>
public sealed record MetadataDto(
    IReadOnlyList<RoleOptionDto> Roles,
    IReadOnlyList<StatusOptionDto> Statuses)
{
    public static MetadataDto Build() => new(
        RoleMetadata.DisplayOrder
            .Select(r => new RoleOptionDto(r, r.ToString(), RoleMetadata.Label(r), RoleMetadata.Color(r)))
            .ToList(),
        Enum.GetValues<BoardStatus>()
            .Select(s => new StatusOptionDto(s, s.ToString(), BoardStatusMetadata.Label(s),
                BoardStatusMetadata.Color(s)))
            .ToList());
}

public sealed record RoleOptionDto(Role Value, string Name, string Label, string Color);

public sealed record StatusOptionDto(BoardStatus Value, string Name, string Label, string Color);
