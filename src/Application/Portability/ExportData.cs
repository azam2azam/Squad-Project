using Application.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Portability;

/// <summary>Bulk export of every board and the whole roster.</summary>
public sealed record ExportDataQuery : IRequest<BoardExportFile>;

public sealed class ExportDataQueryHandler(IAppDbContext db)
    : IRequestHandler<ExportDataQuery, BoardExportFile>
{
    public async Task<BoardExportFile> Handle(ExportDataQuery request, CancellationToken cancellationToken)
    {
        var boards = await db.Boards
            .Include(b => b.Members)
            .OrderBy(b => b.OrderIndex)
            .ToListAsync(cancellationToken);

        // The whole roster travels, including deactivated people: a board's historical
        // membership would otherwise point at names the import cannot resolve.
        var people = await db.People
            .OrderBy(p => p.FullName)
            .ToListAsync(cancellationToken);

        return new BoardExportFile(
            BoardExportFile.CurrentVersion,
            DateTimeOffset.UtcNow,
            people.Select(p => new ExportedPerson(
                p.Id, p.FullName, p.DefaultRole, p.DefaultDetail,
                p.Email, p.AvatarColorOverride, p.IsActive)).ToList(),
            boards.Select(b => new ExportedBoard(
                b.Id, b.Title, b.Product, b.SquadName, b.Sprint, b.Status,
                b.ProgressPercent, b.BlockerNote, b.Velocity, b.TargetDate,
                b.JiraProjectKey, b.JiraBoardId, b.OrderIndex,
                b.Members.OrderBy(m => m.OrderIndex)
                    .Select(m => new ExportedMember(
                        m.PersonId, m.Role, m.Detail, m.AllocationPercent, m.OrderIndex))
                    .ToList()))
                .ToList());
    }
}
