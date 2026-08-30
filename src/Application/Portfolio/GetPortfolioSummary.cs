using Application.Abstractions;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Portfolio;

/// <summary>
/// Everything the dashboard needs, in one round trip. Aggregated server-side so the
/// client never re-derives numbers that must agree with the boards themselves.
/// </summary>
public sealed record GetPortfolioSummaryQuery : IRequest<PortfolioSummaryDto>;

public sealed record PortfolioSummaryDto(
    PortfolioHeadlineDto Headline,
    IReadOnlyList<StatusSliceDto> StatusBreakdown,
    IReadOnlyList<SquadSummaryDto> Squads,
    IReadOnlyList<RiskEntryDto> RiskRegister,
    IReadOnlyList<RoleTotalDto> RoleTotals,
    IReadOnlyList<BoardAttentionDto> NeedsAttention);

/// <summary>The four numbers a delivery lead looks at first.</summary>
public sealed record PortfolioHeadlineDto(
    int TotalBoards,
    int TotalPeople,
    int AverageProgressPercent,
    int OnTrackPercent,
    int SquadCount,
    int BoardsNeedingAttention);

public sealed record StatusSliceDto(
    BoardStatus Status,
    string Label,
    string Color,
    int Count,
    double Percent,
    /// <summary>
    /// On Track and Delivered are near-identical hues in the inherited palette
    /// (ΔE 5.2, well under the readable floor), so a chart must not rely on colour
    /// alone. This flags the slice that carries a texture as secondary encoding.
    /// </summary>
    bool NeedsTexture);

public sealed record SquadSummaryDto(
    string SquadName,
    int BoardCount,
    int MemberCount,
    int AverageProgressPercent,
    int OnTrackCount,
    int AtRiskCount,
    int BlockedCount,
    int DeliveredCount);

public sealed record RiskEntryDto(
    Guid BoardId,
    string Title,
    string SquadName,
    RiskLevel Level,
    string LevelLabel,
    string LevelColor,
    string? RiskNote,
    BoardStatus Status,
    string StatusLabel,
    int ProgressPercent);

public sealed record RoleTotalDto(Role Role, string Label, string Color, int Count);

/// <summary>A board with something actionable wrong with it.</summary>
public sealed record BoardAttentionDto(
    Guid BoardId,
    string Title,
    string SquadName,
    IReadOnlyList<string> Reasons);

public sealed class GetPortfolioSummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPortfolioSummaryQuery, PortfolioSummaryDto>
{
    public async Task<PortfolioSummaryDto> Handle(
        GetPortfolioSummaryQuery request, CancellationToken cancellationToken)
    {
        var boards = await db.Boards
            .Include(b => b.Members)
            .ThenInclude(m => m.Person)
            .ToListAsync(cancellationToken);

        var activePeople = await db.People.CountAsync(p => p.IsActive, cancellationToken);

        return new PortfolioSummaryDto(
            BuildHeadline(boards, activePeople),
            BuildStatusBreakdown(boards),
            BuildSquads(boards),
            BuildRiskRegister(boards),
            BuildRoleTotals(boards),
            BuildAttention(boards));
    }

    private static PortfolioHeadlineDto BuildHeadline(List<Board> boards, int activePeople)
    {
        if (boards.Count == 0)
        {
            return new PortfolioHeadlineDto(0, activePeople, 0, 0, 0, 0);
        }

        var onTrack = boards.Count(b =>
            b.Status is BoardStatus.OnTrack or BoardStatus.Delivered);

        return new PortfolioHeadlineDto(
            boards.Count,
            activePeople,
            (int)Math.Round(boards.Average(b => b.ProgressPercent)),
            (int)Math.Round(onTrack * 100d / boards.Count),
            boards.Select(b => b.SquadName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            boards.Count(NeedsAttention));
    }

    private static IReadOnlyList<StatusSliceDto> BuildStatusBreakdown(List<Board> boards)
    {
        if (boards.Count == 0) return [];

        return Enum.GetValues<BoardStatus>()
            .Select(status => new
            {
                Status = status,
                Count = boards.Count(b => b.Status == status)
            })
            .Where(x => x.Count > 0)
            .Select(x => new StatusSliceDto(
                x.Status,
                BoardStatusMetadata.Label(x.Status),
                BoardStatusMetadata.Color(x.Status),
                x.Count,
                Math.Round(x.Count * 100d / boards.Count, 1),
                // Delivered shares a hue with On Track; it carries the texture.
                x.Status == BoardStatus.Delivered))
            .ToList();
    }

    private static IReadOnlyList<SquadSummaryDto> BuildSquads(List<Board> boards) =>
        boards
            .GroupBy(b => b.SquadName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SquadSummaryDto(
                group.Key,
                group.Count(),
                // Distinct people: someone on two of a squad's boards is one person.
                group.SelectMany(b => b.Members).Select(m => m.PersonId).Distinct().Count(),
                (int)Math.Round(group.Average(b => b.ProgressPercent)),
                group.Count(b => b.Status == BoardStatus.OnTrack),
                group.Count(b => b.Status == BoardStatus.AtRisk),
                group.Count(b => b.Status == BoardStatus.Blocked),
                group.Count(b => b.Status == BoardStatus.Delivered)))
            // Worst-progress first: the dashboard should lead with what needs looking at.
            .OrderBy(s => s.AverageProgressPercent)
            .ThenBy(s => s.SquadName)
            .ToList();

    private static IReadOnlyList<RiskEntryDto> BuildRiskRegister(List<Board> boards) =>
        boards
            .Where(b => RiskLevelMetadata.IsNotable(b.RiskLevel) || b.Status == BoardStatus.Blocked)
            .OrderByDescending(b => b.RiskLevel)
            .ThenBy(b => b.ProgressPercent)
            .Select(b => new RiskEntryDto(
                b.Id,
                b.Title,
                b.SquadName,
                b.RiskLevel,
                RiskLevelMetadata.Label(b.RiskLevel),
                RiskLevelMetadata.Color(b.RiskLevel),
                // A blocked board with no risk set still belongs on the register; its
                // blocker note is the useful text.
                b.RiskNote ?? b.BlockerNote,
                b.Status,
                BoardStatusMetadata.Label(b.Status),
                b.ProgressPercent))
            .ToList();

    private static IReadOnlyList<RoleTotalDto> BuildRoleTotals(List<Board> boards)
    {
        var assignments = boards.SelectMany(b => b.Members).ToList();

        return RoleMetadata.DisplayOrder
            .Select(role => new RoleTotalDto(
                role,
                RoleMetadata.Label(role),
                RoleMetadata.Color(role),
                assignments.Count(m => m.Role == role)))
            .Where(r => r.Count > 0)
            .ToList();
    }

    private static IReadOnlyList<BoardAttentionDto> BuildAttention(List<Board> boards) =>
        boards
            .Where(NeedsAttention)
            .Select(b => new BoardAttentionDto(b.Id, b.Title, b.SquadName, ReasonsFor(b)))
            .ToList();

    /// <summary>
    /// A board needs attention if it is blocked, carries notable risk, or the squad has
    /// an advisory warning. Deliberately the same rule everywhere so the headline count
    /// and the list below it can never disagree.
    /// </summary>
    private static bool NeedsAttention(Board board) =>
        board.Status == BoardStatus.Blocked
        || RiskLevelMetadata.IsNotable(board.RiskLevel)
        || board.Warnings.Count > 0;

    private static IReadOnlyList<string> ReasonsFor(Board board)
    {
        var reasons = new List<string>();

        if (board.Status == BoardStatus.Blocked)
        {
            reasons.Add(board.BlockerNote is { Length: > 0 } note
                ? $"Blocked: {note}"
                : "Blocked, with no blocker note");
        }

        if (RiskLevelMetadata.IsNotable(board.RiskLevel))
        {
            reasons.Add(board.RiskNote is { Length: > 0 } note
                ? $"{RiskLevelMetadata.Label(board.RiskLevel)} risk: {note}"
                : $"{RiskLevelMetadata.Label(board.RiskLevel)} risk");
        }

        reasons.AddRange(board.Warnings);
        return reasons;
    }
}
