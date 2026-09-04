using Application.Abstractions;
using Application.Portability;
using ClosedXML.Excel;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Portability;

namespace Application.Tests;

/// <summary>
/// The Excel round-trip. These matter more than most: a spreadsheet is edited by hand,
/// so the parser has to cope with what people actually type, and a silent
/// misinterpretation would corrupt real boards.
/// </summary>
public class ExcelWorkbookTests
{
    private readonly ExcelWorkbookSerializer _serializer = new();

    private static readonly Guid PersonId = Guid.NewGuid();
    private static readonly Guid BoardId = Guid.NewGuid();

    private static BoardExportFile SampleFile() => new(
        BoardExportFile.CurrentVersion,
        DateTimeOffset.UtcNow,
        [new ExportedPerson(PersonId, "Aisha Kareem", Role.ProductOwner,
            "Outpatient", "aisha@example.com", null, true)],
        [new ExportedBoard(BoardId, "Triage Rewrite", "VIDA HIS", "Squad One", "Sprint 1",
            BoardStatus.AtRisk, 42, "Waiting on vendor", null,
            new DateOnly(2026, 12, 1), "TRI", "9", 3,
            [new ExportedMember(PersonId, Role.ProductOwner, "PO", 50, 0)],
            RiskLevel.High, "Vendor may slip")]);

    private BoardExportFile RoundTrip(BoardExportFile file)
    {
        var bytes = _serializer.Write(file);
        using var stream = new MemoryStream(bytes);
        return _serializer.Read(stream);
    }

    [Fact]
    public void A_workbook_round_trips_without_losing_anything()
    {
        var result = RoundTrip(SampleFile());

        var board = result.Boards.Should().ContainSingle().Subject;
        board.Id.Should().Be(BoardId);
        board.Title.Should().Be("Triage Rewrite");
        board.SquadName.Should().Be("Squad One");
        board.Status.Should().Be(BoardStatus.AtRisk);
        board.ProgressPercent.Should().Be(42);
        board.RiskLevel.Should().Be(RiskLevel.High);
        board.RiskNote.Should().Be("Vendor may slip");
        board.BlockerNote.Should().Be("Waiting on vendor");
        board.TargetDate.Should().Be(new DateOnly(2026, 12, 1));
        board.JiraProjectKey.Should().Be("TRI");
        board.JiraBoardId.Should().Be("9");
        board.OrderIndex.Should().Be(3);

        var member = board.Members.Should().ContainSingle().Subject;
        member.PersonId.Should().Be(PersonId);
        member.Role.Should().Be(Role.ProductOwner);
        member.AllocationPercent.Should().Be(50);

        var person = result.People.Should().ContainSingle().Subject;
        person.Id.Should().Be(PersonId);
        person.FullName.Should().Be("Aisha Kareem");
        person.IsActive.Should().BeTrue();
    }

    [Fact]
    public void The_workbook_has_the_sheets_a_person_is_told_to_edit()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Select(w => w.Name)
            .Should().Contain(["Boards", "People", "Members", "Read me"]);
    }

    [Fact]
    public void Enums_are_written_as_labels_so_the_sheet_is_readable()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        var boards = workbook.Worksheet("Boards");
        // "At Risk", not "1".
        boards.Cell(2, 6).GetString().Should().Be("At Risk");
        boards.Cell(2, 8).GetString().Should().Be("High");
    }

    [Fact]
    public void An_edit_made_in_the_spreadsheet_is_read_back()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);

        using (var workbook = new XLWorkbook(stream))
        {
            var boards = workbook.Worksheet("Boards");
            boards.Cell(2, 2).Value = "Triage Rewrite (renamed)";
            boards.Cell(2, 6).Value = "Blocked";
            boards.Cell(2, 7).Value = 15;
            boards.Cell(2, 8).Value = "Critical";

            using var edited = new MemoryStream();
            workbook.SaveAs(edited);
            edited.Position = 0;

            var result = _serializer.Read(edited);
            var board = result.Boards.Single();

            board.Id.Should().Be(BoardId, "editing a row must update it, not create a new one");
            board.Title.Should().Be("Triage Rewrite (renamed)");
            board.Status.Should().Be(BoardStatus.Blocked);
            board.ProgressPercent.Should().Be(15);
            board.RiskLevel.Should().Be(RiskLevel.Critical);
        }
    }

    [Fact]
    public void A_new_row_with_a_blank_id_gets_one_generated()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);

        using var workbook = new XLWorkbook(stream);
        var boards = workbook.Worksheet("Boards");

        // How somebody adds a board in Excel: type a row, leave Id empty.
        boards.Cell(3, 2).Value = "Added In Excel";
        boards.Cell(3, 3).Value = "VIDA HIS";
        boards.Cell(3, 4).Value = "Squad Two";
        boards.Cell(3, 6).Value = "On Track";
        boards.Cell(3, 7).Value = 5;

        using var edited = new MemoryStream();
        workbook.SaveAs(edited);
        edited.Position = 0;

        var result = _serializer.Read(edited);

        result.Boards.Should().HaveCount(2);
        var added = result.Boards.Single(b => b.Title == "Added In Excel");
        added.Id.Should().NotBeEmpty();
        added.Id.Should().NotBe(BoardId);
        added.SquadName.Should().Be("Squad Two");
    }

    [Fact]
    public void Enum_cells_accept_the_label_the_name_or_the_number()
    {
        foreach (var (typed, expected) in new[]
                 {
                     ("At Risk", BoardStatus.AtRisk),
                     ("AtRisk", BoardStatus.AtRisk),
                     ("1", BoardStatus.AtRisk),
                     ("blocked", BoardStatus.Blocked)
                 })
        {
            var bytes = _serializer.Write(SampleFile());
            using var stream = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(stream);

            workbook.Worksheet("Boards").Cell(2, 6).Value = typed;

            using var edited = new MemoryStream();
            workbook.SaveAs(edited);
            edited.Position = 0;

            _serializer.Read(edited).Boards.Single().Status
                .Should().Be(expected, $"'{typed}' should be understood");
        }
    }

    [Fact]
    public void An_out_of_range_progress_names_the_row_rather_than_silently_clamping()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheet("Boards").Cell(2, 7).Value = 150;

        using var edited = new MemoryStream();
        workbook.SaveAs(edited);
        edited.Position = 0;

        var act = () => _serializer.Read(edited);

        // Silently clamping would quietly change a number somebody typed.
        act.Should().Throw<WorkbookFormatException>()
            .WithMessage("*row 2*150*");
    }

    [Fact]
    public void An_unrecognised_status_lists_what_is_allowed()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheet("Boards").Cell(2, 6).Value = "Nearly done";

        using var edited = new MemoryStream();
        workbook.SaveAs(edited);
        edited.Position = 0;

        var act = () => _serializer.Read(edited);

        act.Should().Throw<WorkbookFormatException>()
            .WithMessage("*Nearly done*On Track*");
    }

    [Fact]
    public void A_workbook_missing_the_boards_sheet_says_so()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Something else");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var act = () => _serializer.Read(stream);

        act.Should().Throw<WorkbookFormatException>().WithMessage("*People*");
    }

    [Fact]
    public void A_file_that_is_not_a_spreadsheet_is_refused_readably()
    {
        using var stream = new MemoryStream("this is not a workbook"u8.ToArray());

        var act = () => _serializer.Read(stream);

        act.Should().Throw<WorkbookFormatException>()
            .WithMessage("*could not be opened as an Excel workbook*");
    }

    [Fact]
    public void Removing_a_member_row_takes_that_person_off_the_squad()
    {
        var bytes = _serializer.Write(SampleFile());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheet("Members").Row(2).Delete();

        using var edited = new MemoryStream();
        workbook.SaveAs(edited);
        edited.Position = 0;

        _serializer.Read(edited).Boards.Single().Members.Should().BeEmpty();
    }

    [Fact]
    public void A_deactivated_person_round_trips_as_inactive()
    {
        var file = SampleFile() with
        {
            People = [new ExportedPerson(PersonId, "Aisha Kareem", Role.ProductOwner,
                null, null, null, false)]
        };

        RoundTrip(file).People.Single().IsActive.Should().BeFalse();
    }
}
