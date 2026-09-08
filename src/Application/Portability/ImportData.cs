using Application.Abstractions;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Portability;

/// <summary>
/// Bulk import of boards and roster. Upserts by id, so re-importing the same file is a
/// no-op rather than a duplication — the prototype's Load, made safe to repeat.
/// </summary>
public sealed record ImportDataCommand(BoardExportFile File) : IRequest<ImportResult>;

public sealed class ImportDataCommandValidator : AbstractValidator<ImportDataCommand>
{
    public ImportDataCommandValidator()
    {
        RuleFor(c => c.File).NotNull();
        RuleFor(c => c.File.Version)
            .Equal(BoardExportFile.CurrentVersion)
            .WithMessage($"Unsupported file version. This build reads version {BoardExportFile.CurrentVersion}.");
    }
}

public sealed class ImportDataCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, IBoardAuthorizer authorizer)
    : IRequestHandler<ImportDataCommand, ImportResult>
{
    public async Task<ImportResult> Handle(ImportDataCommand request, CancellationToken cancellationToken)
    {
        // A bulk import can rewrite every board, so it is admin-only.
        authorizer.EnsureIsAdmin();

        var file = request.File;
        var warnings = new List<string>();

        var (peopleById, peopleCreated, peopleUpdated) =
            await UpsertPeopleAsync(file, cancellationToken);

        var boardsCreated = 0;
        var boardsUpdated = 0;
        var membersLinked = 0;

        // Categories are matched by name and created on demand. That is what makes
        // categorising a whole portfolio a single spreadsheet edit rather than opening
        // every board — and each one created is reported, so a typo shows up as a new
        // category in the result instead of vanishing silently.
        var categories = await db.BoardCategories
            .ToDictionaryAsync(c => c.Name.ToLowerInvariant(), c => c, cancellationToken);

        foreach (var incoming in file.Boards)
        {
            var board = await db.Boards
                .IgnoreQueryFilters()
                .Include(b => b.Members)
                .FirstOrDefaultAsync(b => b.Id == incoming.Id, cancellationToken);

            if (board is null)
            {
                board = new Board(incoming.Title, incoming.Product, incoming.SquadName,
                    incoming.Sprint, incoming.Status, incoming.ProgressPercent,
                    currentUser.DisplayName, incoming.OrderIndex);
                board.AssignIdForImport(incoming.Id);
                db.Boards.Add(board);
                boardsCreated++;
            }
            else
            {
                boardsUpdated++;
            }

            board.UpdateMeta(incoming.Title, incoming.Product, incoming.SquadName,
                incoming.Sprint, incoming.Status, incoming.ProgressPercent,
                incoming.BlockerNote, incoming.Velocity, incoming.TargetDate,
                incoming.JiraProjectKey, incoming.JiraBoardId,
                incoming.RiskLevel, incoming.RiskNote);
            board.SetOrder(incoming.OrderIndex);
            board.AssignCategory(
                ResolveCategory(incoming.CategoryName, categories, warnings));

            membersLinked += await SyncMembersAsync(board, incoming, peopleById, warnings, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ImportResult(peopleCreated, peopleUpdated, boardsCreated,
            boardsUpdated, membersLinked, warnings);
    }

    private async Task<(Dictionary<Guid, Person> People, int Created, int Updated)> UpsertPeopleAsync(
        BoardExportFile file, CancellationToken cancellationToken)
    {
        var incomingIds = file.People.Select(p => p.Id).ToList();

        var existing = await db.People
            .Where(p => incomingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var created = 0;
        var updated = 0;

        foreach (var incoming in file.People)
        {
            if (existing.TryGetValue(incoming.Id, out var person))
            {
                person.Update(incoming.FullName, incoming.DefaultRole, incoming.DefaultDetail,
                    incoming.Email, incoming.AvatarColorOverride);
                updated++;
            }
            else
            {
                person = new Person(incoming.FullName, incoming.DefaultRole,
                    incoming.DefaultDetail, incoming.Email, incoming.AvatarColorOverride);
                person.AssignIdForImport(incoming.Id);
                db.People.Add(person);
                existing[incoming.Id] = person;
                created++;
            }

            // Applied after the upsert so an imported leaver stays deactivated.
            if (!incoming.IsActive)
            {
                person.Deactivate();
            }
            else
            {
                person.Reactivate();
            }
        }

        return (existing, created, updated);
    }

    /// <summary>
    /// Finds a category by name, creating one when the spreadsheet names a programme that
    /// does not exist yet. Blank clears the board's category, which is how a board is
    /// taken out of a programme from the spreadsheet.
    /// </summary>
    private Guid? ResolveCategory(
        string? name, Dictionary<string, BoardCategory> categories, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var trimmed = name.Trim();
        var key = trimmed.ToLowerInvariant();

        if (categories.TryGetValue(key, out var existing)) return existing.Id;

        // Colour is a placeholder an admin can change on the Categories screen; refusing
        // the import over a missing colour would be worse than picking a neutral one.
        var created = new BoardCategory(trimmed, null, "#8595A9", categories.Count);
        db.BoardCategories.Add(created);
        categories[key] = created;

        warnings.Add($"Created a new category \"{trimmed}\" from the Category column.");
        return created.Id;
    }

    /// <summary>
    /// Makes the board's membership match the file exactly: drops members no longer
    /// listed, adds the new ones, and updates the rest in place.
    /// </summary>
    private async Task<int> SyncMembersAsync(
        Board board,
        ExportedBoard incoming,
        Dictionary<Guid, Person> peopleById,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var linked = 0;
        var wanted = incoming.Members.ToList();

        foreach (var stale in board.Members
                     .Where(m => wanted.All(w => w.PersonId != m.PersonId))
                     .ToList())
        {
            board.RemoveMember(stale.Id);
            db.SquadMembers.Remove(stale);
        }

        foreach (var wantedMember in wanted.OrderBy(m => m.OrderIndex))
        {
            if (!peopleById.TryGetValue(wantedMember.PersonId, out var person))
            {
                // The file may reference somebody already in the database but absent
                // from its own people list; fall back to a lookup before giving up.
                person = await db.People
                    .FirstOrDefaultAsync(p => p.Id == wantedMember.PersonId, cancellationToken);

                if (person is null)
                {
                    warnings.Add(
                        $"'{incoming.Title}' references an unknown person ({wantedMember.PersonId}); that member was skipped.");
                    continue;
                }

                peopleById[person.Id] = person;
            }

            var existing = board.Members.FirstOrDefault(m => m.PersonId == person.Id);
            if (existing is not null)
            {
                existing.Update(wantedMember.Role, wantedMember.Detail, wantedMember.AllocationPercent);
                existing.SetOrder(wantedMember.OrderIndex);
            }
            else
            {
                // Deliberately bypasses Board.AddMember's "must be active" rule: an
                // imported historical board can legitimately contain people who have
                // since left, and dropping them would rewrite history.
                var member = new SquadMember(board.Id, person, wantedMember.Role,
                    wantedMember.Detail, wantedMember.AllocationPercent, wantedMember.OrderIndex);

                board.AddMemberForImport(member);
                db.SquadMembers.Add(member);
            }

            linked++;
        }

        board.Touch();
        return linked;
    }
}
