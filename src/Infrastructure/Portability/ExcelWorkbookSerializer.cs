using Application.Abstractions;
using Application.Portability;
using ClosedXML.Excel;
using Domain.Enums;

namespace Infrastructure.Portability;

/// <summary>
/// Excel round-trip for the whole portfolio.
///
/// Three sheets, because that is how the data actually relates: Boards, People, and
/// Members joining the two. Ids are carried in the sheets so an export can be edited and
/// re-imported as an update rather than landing as duplicates — the same upsert-by-id
/// contract the JSON import uses.
///
/// Enums are written as their **labels**, not their numbers, because a person editing a
/// spreadsheet should see "At Risk", not "1". Reading accepts either.
/// </summary>
public sealed class ExcelWorkbookSerializer : IWorkbookSerializer
{
    private const string BoardsSheet = "Boards";
    private const string PeopleSheet = "People";
    private const string MembersSheet = "Members";

    private static readonly string[] BoardHeaders =
    [
        "Id", "Title", "Product", "Squad", "Sprint", "Status", "Progress %",
        "Risk", "Risk note", "Blocker note", "Target date",
        "Jira project key", "Jira board id", "Order"
    ];

    private static readonly string[] PeopleHeaders =
    [
        "Id", "Full name", "Default role", "Default detail", "Email", "Active"
    ];

    private static readonly string[] MemberHeaders =
    [
        "Board id", "Board title", "Person id", "Person name", "Role", "Detail",
        "Allocation %", "Order"
    ];

    public byte[] Write(BoardExportFile file)
    {
        using var workbook = new XLWorkbook();

        WriteBoards(workbook, file);
        WritePeople(workbook, file);
        WriteMembers(workbook, file);
        WriteReadme(workbook, file);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public BoardExportFile Read(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            throw new WorkbookFormatException(
                "That file could not be opened as an Excel workbook (.xlsx). " +
                $"({ex.GetType().Name})");
        }

        using (workbook)
        {
            var people = ReadPeople(workbook);
            var members = ReadMembers(workbook);
            var boards = ReadBoards(workbook, members);

            return new BoardExportFile(
                BoardExportFile.CurrentVersion,
                DateTimeOffset.UtcNow,
                people,
                boards);
        }
    }

    // -----------------------------------------------------------------------
    // Write
    // -----------------------------------------------------------------------

    private static void WriteBoards(XLWorkbook workbook, BoardExportFile file)
    {
        var sheet = workbook.Worksheets.Add(BoardsSheet);
        WriteHeader(sheet, BoardHeaders);

        var row = 2;
        foreach (var board in file.Boards)
        {
            sheet.Cell(row, 1).Value = board.Id.ToString();
            sheet.Cell(row, 2).Value = board.Title;
            sheet.Cell(row, 3).Value = board.Product;
            sheet.Cell(row, 4).Value = board.SquadName;
            sheet.Cell(row, 5).Value = board.Sprint ?? string.Empty;
            sheet.Cell(row, 6).Value = BoardStatusMetadata.Label(board.Status);
            sheet.Cell(row, 7).Value = board.ProgressPercent;
            sheet.Cell(row, 8).Value = RiskLevelMetadata.Label(board.RiskLevel);
            sheet.Cell(row, 9).Value = board.RiskNote ?? string.Empty;
            sheet.Cell(row, 10).Value = board.BlockerNote ?? string.Empty;
            sheet.Cell(row, 11).Value = board.TargetDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            sheet.Cell(row, 12).Value = board.JiraProjectKey ?? string.Empty;
            sheet.Cell(row, 13).Value = board.JiraBoardId ?? string.Empty;
            sheet.Cell(row, 14).Value = board.OrderIndex;
            row++;
        }

        Finish(sheet, BoardHeaders.Length);
    }

    private static void WritePeople(XLWorkbook workbook, BoardExportFile file)
    {
        var sheet = workbook.Worksheets.Add(PeopleSheet);
        WriteHeader(sheet, PeopleHeaders);

        var row = 2;
        foreach (var person in file.People)
        {
            sheet.Cell(row, 1).Value = person.Id.ToString();
            sheet.Cell(row, 2).Value = person.FullName;
            sheet.Cell(row, 3).Value = RoleMetadata.Label(person.DefaultRole);
            sheet.Cell(row, 4).Value = person.DefaultDetail ?? string.Empty;
            sheet.Cell(row, 5).Value = person.Email ?? string.Empty;
            sheet.Cell(row, 6).Value = person.IsActive ? "Yes" : "No";
            row++;
        }

        Finish(sheet, PeopleHeaders.Length);
    }

    private static void WriteMembers(XLWorkbook workbook, BoardExportFile file)
    {
        var sheet = workbook.Worksheets.Add(MembersSheet);
        WriteHeader(sheet, MemberHeaders);

        // Names are denormalised into this sheet purely so it is readable on its own.
        // Only the ids are read back.
        var peopleById = file.People.ToDictionary(p => p.Id, p => p.FullName);

        var row = 2;
        foreach (var board in file.Boards)
        {
            foreach (var member in board.Members)
            {
                sheet.Cell(row, 1).Value = board.Id.ToString();
                sheet.Cell(row, 2).Value = board.Title;
                sheet.Cell(row, 3).Value = member.PersonId.ToString();
                sheet.Cell(row, 4).Value = peopleById.TryGetValue(member.PersonId, out var name)
                    ? name
                    : string.Empty;
                sheet.Cell(row, 5).Value = RoleMetadata.Label(member.Role);
                sheet.Cell(row, 6).Value = member.Detail ?? string.Empty;
                sheet.Cell(row, 7).Value = member.AllocationPercent.HasValue
                    ? member.AllocationPercent.Value
                    : Blank.Value;
                sheet.Cell(row, 8).Value = member.OrderIndex;
                row++;
            }
        }

        Finish(sheet, MemberHeaders.Length);
    }

    /// <summary>A short instruction sheet, so the file explains itself when emailed around.</summary>
    private static void WriteReadme(XLWorkbook workbook, BoardExportFile file)
    {
        var sheet = workbook.Worksheets.Add("Read me");

        var lines = new (string Heading, string Body)[]
        {
            ("Squad Status Board export",
                $"Exported {file.ExportedAt:yyyy-MM-dd HH:mm} UTC · format version {file.Version}"),
            ("How to use this file",
                "Edit the Boards, People and Members sheets, then import the file back in. " +
                "Rows are matched on Id, so editing a row updates it rather than creating a duplicate."),
            ("Adding new rows",
                "Leave Id blank on a new row and one will be generated on import."),
            ("Deleting",
                "Removing a row from Members takes that person off that squad. Removing a board " +
                "row does NOT delete the board — delete it in the app instead, so the audit trail is kept."),
            ("Status values",
                string.Join(", ", Enum.GetValues<BoardStatus>().Select(BoardStatusMetadata.Label))),
            ("Risk values",
                string.Join(", ", RiskLevelMetadata.DisplayOrder.Select(RiskLevelMetadata.Label))),
            ("Role values",
                string.Join(", ", RoleMetadata.DisplayOrder.Select(RoleMetadata.Label)))
        };

        var row = 1;
        foreach (var (heading, body) in lines)
        {
            sheet.Cell(row, 1).Value = heading;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row + 1, 1).Value = body;
            row += 3;
        }

        sheet.Column(1).Width = 110;
        sheet.Column(1).Style.Alignment.WrapText = true;
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16202D");
            cell.Style.Font.FontColor = XLColor.FromHtml("#EAF1F8");
        }
    }

    private static void Finish(IXLWorksheet sheet, int columnCount)
    {
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, 1, columnCount).SetAutoFilter();
        sheet.Columns(1, columnCount).AdjustToContents(1, 200);
    }

    // -----------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------

    private static IReadOnlyList<ExportedPerson> ReadPeople(XLWorkbook workbook)
    {
        var sheet = RequireSheet(workbook, PeopleSheet);
        var people = new List<ExportedPerson>();

        foreach (var row in DataRows(sheet))
        {
            var name = Text(row, 2);
            if (string.IsNullOrWhiteSpace(name)) continue;

            people.Add(new ExportedPerson(
                ParseIdOrNew(row, 1, PeopleSheet),
                name,
                ParseRole(Text(row, 3), PeopleSheet, row.RowNumber(), "Default role"),
                NullIfBlank(Text(row, 4)),
                NullIfBlank(Text(row, 5)),
                null,
                !Text(row, 6).Equals("No", StringComparison.OrdinalIgnoreCase)));
        }

        return people;
    }

    private static Dictionary<Guid, List<ExportedMember>> ReadMembers(XLWorkbook workbook)
    {
        var result = new Dictionary<Guid, List<ExportedMember>>();

        // Members are optional: a workbook of boards alone is a legitimate edit.
        var sheet = workbook.Worksheets.FirstOrDefault(s => Matches(s.Name, MembersSheet));
        if (sheet is null) return result;

        foreach (var row in DataRows(sheet))
        {
            var boardIdText = Text(row, 1);
            var personIdText = Text(row, 3);
            if (string.IsNullOrWhiteSpace(boardIdText) || string.IsNullOrWhiteSpace(personIdText))
            {
                continue;
            }

            var boardId = ParseId(boardIdText, MembersSheet, row.RowNumber(), "Board id");
            var personId = ParseId(personIdText, MembersSheet, row.RowNumber(), "Person id");

            var member = new ExportedMember(
                personId,
                ParseRole(Text(row, 5), MembersSheet, row.RowNumber(), "Role"),
                NullIfBlank(Text(row, 6)),
                ParseNullableInt(Text(row, 7)),
                ParseNullableInt(Text(row, 8)) ?? 0);

            if (!result.TryGetValue(boardId, out var list))
            {
                list = [];
                result[boardId] = list;
            }

            list.Add(member);
        }

        return result;
    }

    private static IReadOnlyList<ExportedBoard> ReadBoards(
        XLWorkbook workbook, Dictionary<Guid, List<ExportedMember>> membersByBoard)
    {
        var sheet = RequireSheet(workbook, BoardsSheet);
        var boards = new List<ExportedBoard>();

        foreach (var row in DataRows(sheet))
        {
            var title = Text(row, 2);
            if (string.IsNullOrWhiteSpace(title)) continue;

            var id = ParseIdOrNew(row, 1, BoardsSheet);

            boards.Add(new ExportedBoard(
                id,
                title,
                Fallback(Text(row, 3), "Unspecified"),
                Fallback(Text(row, 4), "Unassigned"),
                NullIfBlank(Text(row, 5)),
                ParseEnumByLabel(Text(row, 6), Enum.GetValues<BoardStatus>(),
                    BoardStatusMetadata.Label, BoardStatus.OnTrack,
                    BoardsSheet, row.RowNumber(), "Status"),
                ClampPercent(ParseNullableInt(Text(row, 7)) ?? 0, row.RowNumber()),
                NullIfBlank(Text(row, 10)),
                null,
                ParseNullableDate(Text(row, 11), row.RowNumber()),
                NullIfBlank(Text(row, 12)),
                NullIfBlank(Text(row, 13)),
                ParseNullableInt(Text(row, 14)) ?? 0,
                membersByBoard.TryGetValue(id, out var members) ? members : [],
                ParseEnumByLabel(Text(row, 8), RiskLevelMetadata.DisplayOrder,
                    RiskLevelMetadata.Label, RiskLevel.None, BoardsSheet, row.RowNumber(), "Risk"),
                NullIfBlank(Text(row, 9))));
        }

        return boards;
    }

    // -----------------------------------------------------------------------
    // Cell helpers
    // -----------------------------------------------------------------------

    private static IXLWorksheet RequireSheet(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(s => Matches(s.Name, name))
        ?? throw new WorkbookFormatException(
            $"The workbook has no '{name}' sheet. Export a file first and edit that, " +
            "so the sheets and headers match.");

    private static bool Matches(string actual, string expected) =>
        actual.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IXLRangeRow> DataRows(IXLWorksheet sheet)
    {
        var used = sheet.RangeUsed();
        if (used is null) yield break;

        // Row 1 is the header.
        foreach (var row in used.RowsUsed().Skip(1))
        {
            yield return row;
        }
    }

    private static string Text(IXLRangeRow row, int column) =>
        column <= row.CellCount() ? row.Cell(column).GetString().Trim() : string.Empty;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static Guid ParseIdOrNew(IXLRangeRow row, int column, string sheet)
    {
        var text = Text(row, column);
        // A blank id means "this is a new row" — that is how somebody adds a board in Excel.
        return string.IsNullOrWhiteSpace(text)
            ? Guid.NewGuid()
            : ParseId(text, sheet, row.RowNumber(), "Id");
    }

    private static Guid ParseId(string text, string sheet, int rowNumber, string column) =>
        Guid.TryParse(text, out var id)
            ? id
            : throw new WorkbookFormatException(
                $"{sheet}!{column} on row {rowNumber} is not a valid id ('{text}'). " +
                "Leave it blank to create a new row, or paste the id from an export.");

    private static int? ParseNullableInt(string text) =>
        int.TryParse(text, out var value) ? value : null;

    private static DateOnly? ParseNullableDate(string text, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateOnly.TryParse(text, out var date)) return date;

        // Excel may hand back a serial date as a number.
        if (double.TryParse(text, out var serial))
        {
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        }

        throw new WorkbookFormatException(
            $"Boards!Target date on row {rowNumber} is not a date ('{text}'). Use YYYY-MM-DD.");
    }

    private static int ClampPercent(int value, int rowNumber) =>
        value is >= 0 and <= 100
            ? value
            : throw new WorkbookFormatException(
                $"Boards!Progress % on row {rowNumber} is {value}. It must be between 0 and 100.");

    /// <summary>
    /// Matches a cell against enum labels, then against the enum name, then the number —
    /// so a hand-edited "At Risk", a pasted "AtRisk" and a raw "1" all work.
    /// </summary>
    /// <summary>
    /// Roles are no longer a fixed enum — an admin can add them — so they are matched
    /// against the live catalogue rather than <c>Enum.IsDefined</c>, which would refuse
    /// every custom role. Accepts the display label, the identifier, or the number, so a
    /// file exported before a rename still imports.
    /// </summary>
    private static Role ParseRole(string text, string sheet, int rowNumber, string column)
    {
        var catalogue = RoleMetadata.All;

        if (string.IsNullOrWhiteSpace(text))
        {
            // A blank cell is not an error: it means "the default", as elsewhere.
            return Role.Developer;
        }

        var trimmed = text.Trim();

        var byLabel = catalogue.FirstOrDefault(
            r => r.Label.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (byLabel is not null) return byLabel.Role;

        var collapsed = trimmed.Replace(" ", string.Empty);
        var byName = catalogue.FirstOrDefault(
            r => r.Name.Equals(collapsed, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName.Role;

        if (int.TryParse(trimmed, out var number))
        {
            var byValue = catalogue.FirstOrDefault(r => (int)r.Role == number);
            if (byValue is not null) return byValue.Role;
        }

        throw new WorkbookFormatException(
            $"{sheet}!{column} on row {rowNumber} is '{trimmed}', which is not a recognised role. " +
            $"Allowed: {string.Join(", ", catalogue.Select(r => r.Label))}.");
    }

    private static TEnum ParseEnumByLabel<TEnum>(
        string text,
        IEnumerable<TEnum> candidates,
        Func<TEnum, string> label,
        TEnum fallback,
        string sheet,
        int rowNumber,
        string column)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        var list = candidates.ToList();

        var byLabel = list.FirstOrDefault(
            c => label(c).Equals(text, StringComparison.OrdinalIgnoreCase));
        if (!byLabel.Equals(default(TEnum)) || label(byLabel).Equals(text, StringComparison.OrdinalIgnoreCase))
        {
            return byLabel;
        }

        if (Enum.TryParse<TEnum>(text.Replace(" ", string.Empty), true, out var byName)
            && Enum.IsDefined(byName))
        {
            return byName;
        }

        if (int.TryParse(text, out var number)
            && Enum.IsDefined(typeof(TEnum), number))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), number);
        }

        throw new WorkbookFormatException(
            $"{sheet}!{column} on row {rowNumber} is '{text}', which is not a recognised value. " +
            $"Allowed: {string.Join(", ", list.Select(label))}.");
    }
}
