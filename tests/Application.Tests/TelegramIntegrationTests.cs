using Domain.Common;
using Application.Abstractions;
using Application.Telegram;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// The parser and the authorisation path, which are the two places this integration could
/// do real damage: one writes to boards from a chat window, the other decides who is
/// allowed to. Everything here runs without Telegram — the pipeline is deliberately
/// separable from the transport.
/// </summary>
public sealed class TelegramParserTests
{
    [Theory]
    [InlineData("status: On Track", BoardStatus.OnTrack)]
    [InlineData("status: at risk", BoardStatus.AtRisk)]
    [InlineData("Status = BLOCKED", BoardStatus.Blocked)]
    [InlineData("status - in review", BoardStatus.InReview)]
    [InlineData("state: delivered", BoardStatus.Delivered)]
    [InlineData("status: amber", BoardStatus.AtRisk)]
    [InlineData("status: stuck", BoardStatus.Blocked)]
    public void Reads_status_however_it_is_written(string line, BoardStatus expected)
    {
        TelegramMessageParser.Parse($"#update DIS\n{line}").Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("progress: 65", 65)]
    [InlineData("progress: 65%", 65)]
    [InlineData("progress: 65 percent", 65)]
    [InlineData("complete: 0.65", 65)]
    [InlineData("done: 100", 100)]
    [InlineData("progress: 0", 0)]
    public void Reads_progress_however_it_is_written(string line, int expected)
    {
        TelegramMessageParser.Parse($"#update DIS\n{line}").ProgressPercent.Should().Be(expected);
    }

    [Fact]
    public void Rejects_a_progress_outside_the_range_rather_than_clamping_it()
    {
        // Clamping would turn a typo into a plausible-looking number nobody typed.
        var parsed = TelegramMessageParser.Parse("#update DIS\nprogress: 650");

        parsed.ProgressPercent.Should().BeNull();
        parsed.Unrecognised.Should().ContainSingle();
    }

    [Fact]
    public void Reads_the_board_from_the_hash_tag()
    {
        TelegramMessageParser.Parse("#update DIS\nstatus: blocked").BoardReference.Should().Be("DIS");
        TelegramMessageParser.Parse("#DIS\nstatus: blocked").BoardReference.Should().Be("DIS");
        TelegramMessageParser.Parse("board: DIS\nstatus: blocked").BoardReference.Should().Be("DIS");
    }

    [Fact]
    public void Reads_the_board_from_a_bare_first_line()
    {
        var parsed = TelegramMessageParser.Parse("Discharge Revamp\nprogress: 40");

        parsed.BoardReference.Should().Be("Discharge Revamp");
        parsed.ProgressPercent.Should().Be(40);
    }

    [Fact]
    public void Keeps_a_hyphenated_title_intact()
    {
        // The hyphen rule exists for this: a title with a dash in it is not a key/value pair.
        var parsed = TelegramMessageParser.Parse("Discharge Revamp - Gaps for MOH\nprogress: 40");

        parsed.BoardReference.Should().Be("Discharge Revamp - Gaps for MOH");
    }

    [Fact]
    public void Splits_a_hyphenated_field_because_the_key_is_one_word()
    {
        TelegramMessageParser.Parse("#update DIS\nblocker - waiting on the vendor")
            .BlockerNote.Should().Be("waiting on the vendor");
    }

    [Fact]
    public void Collects_lines_it_did_not_understand_instead_of_failing()
    {
        var parsed = TelegramMessageParser.Parse(
            "#update DIS\nstatus: blocked\nthoughts and prayers\nprogress: 20");

        parsed.Status.Should().Be(BoardStatus.Blocked);
        parsed.ProgressPercent.Should().Be(20);
        parsed.Unrecognised.Should().ContainSingle().Which.Should().Be("thoughts and prayers");
    }

    [Fact]
    public void Reads_a_command_and_its_argument()
    {
        var parsed = TelegramMessageParser.Parse("/start 4KDP2X8A");

        parsed.IsCommand.Should().BeTrue();
        parsed.Command.Should().Be("/start");
        parsed.CommandArgument.Should().Be("4KDP2X8A");
    }

    [Fact]
    public void Reads_a_command_addressed_to_the_bot_in_a_group()
    {
        // In a group people type "/help@squadbot", which is the same command.
        TelegramMessageParser.Parse("/help@squadbot").Command.Should().Be("/help");
    }

    [Fact]
    public void An_empty_message_asks_for_nothing()
    {
        var parsed = TelegramMessageParser.Parse("   ");

        parsed.HasFields.Should().BeFalse();
        parsed.BoardReference.Should().BeNull();
        parsed.IsCommand.Should().BeFalse();
    }
}

public sealed class TelegramBoardResolverTests
{
    private static Board Make(string title, string? code, string product = "Discharge")
    {
        var board = new Board(title, product, "Aurora", null, BoardStatus.OnTrack, 0, "tester");
        board.AssignCode(code);
        return board;
    }

    [Fact]
    public void Prefers_the_code_over_a_title_that_also_matches()
    {
        var byCode = Make("Something else entirely", "DIS");
        var byTitle = Make("DIS is in this title", null);

        TelegramBoardResolver.Resolve([byCode, byTitle], "DIS").Board.Should().Be(byCode);
    }

    [Fact]
    public void Matches_a_title_prefix_when_no_code_does()
    {
        var board = Make("Discharge Revamp - Gaps for MOH", null);

        TelegramBoardResolver.Resolve([board], "Discharge").Board.Should().Be(board);
    }

    [Fact]
    public void Refuses_rather_than_guessing_when_several_match()
    {
        var one = Make("Discharge Revamp", null);
        var two = Make("Discharge Summary", null);

        var match = TelegramBoardResolver.Resolve([one, two], "Discharge");

        match.Board.Should().BeNull();
        match.Message.Should().Contain("2 boards");
    }

    [Fact]
    public void Says_so_when_nothing_matches()
    {
        var match = TelegramBoardResolver.Resolve([Make("Discharge Revamp", "DIS")], "PHARMACY");

        match.Board.Should().BeNull();
        match.Message.Should().Contain("/boards");
    }

    [Fact]
    public void Asks_which_board_when_none_was_named()
    {
        TelegramBoardResolver.Resolve([Make("Discharge Revamp", "DIS")], null)
            .Message.Should().Contain("Which board");
    }
}

public sealed class TelegramPipelineTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private ProcessTelegramMessageCommandHandler Handler =>
        new(_harness.Db, _harness.Notifier);

    private static TelegramInboundMessage From(long senderId, string text) =>
        new(UpdateId: 1, ChatId: 55, ChatType: "private", SenderId: senderId,
            SenderName: "Shehan", SenderUsername: "shehan", Text: text);

    private AppUser SeedUser(UserRole role = UserRole.Admin, string name = "Shehan Cooray")
    {
        var user = new AppUser($"{name.Replace(' ', '.').ToLowerInvariant()}@pirt.example", name,
            role, "hash");
        _harness.Db.Users.Add(user);
        _harness.Db.SaveChanges();
        return user;
    }

    private Board SeedBoard(string title = "Discharge Revamp", string code = "DIS",
        Guid? ownerId = null)
    {
        var board = new Board(title, "Discharge", "Aurora", "Q3", BoardStatus.OnTrack, 40, "tester");
        board.AssignCode(code);
        if (ownerId is { } id) board.AssignOwner(id);

        _harness.Db.Boards.Add(board);
        _harness.Db.SaveChanges();
        return board;
    }

    private void Link(long telegramUserId, AppUser user)
    {
        _harness.Db.TelegramLinks.Add(new TelegramLink(telegramUserId, "shehan",
            user.DisplayName, user.Id));
        _harness.Db.SaveChanges();
    }

    [Fact]
    public async Task An_unlinked_sender_changes_nothing_and_is_told_how_to_enrol()
    {
        SeedBoard();

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(999, "#update DIS\nstatus: blocked")), default);

        reply.Outcome.Should().Be(TelegramOutcome.SenderNotLinked);
        reply.Text.Should().Contain("/start");
        (await _harness.Db.Boards.FirstAsync()).Status.Should().Be(BoardStatus.OnTrack);
    }

    [Fact]
    public async Task A_revoked_link_is_refused_exactly_like_an_unknown_sender()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1001, user);

        var link = await _harness.Db.TelegramLinks.FirstAsync();
        link.Revoke();
        await _harness.Db.SaveChangesAsync();

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1001, "#update DIS\nstatus: blocked")), default);

        reply.Outcome.Should().Be(TelegramOutcome.SenderNotLinked);
    }

    [Fact]
    public async Task A_viewer_cannot_change_a_board_from_telegram()
    {
        var user = SeedUser(UserRole.Viewer);
        SeedBoard();
        Link(1002, user);

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1002, "#update DIS\nstatus: blocked")), default);

        reply.Outcome.Should().Be(TelegramOutcome.NotPermitted);
        (await _harness.Db.Boards.FirstAsync()).Status.Should().Be(BoardStatus.OnTrack);
    }

    [Fact]
    public async Task A_product_owner_cannot_change_a_board_they_do_not_own()
    {
        var owner = SeedUser(UserRole.ProductOwner, "Pradeep S");
        var other = SeedUser(UserRole.ProductOwner, "Udith Perera");
        SeedBoard(ownerId: owner.Id);
        Link(1003, other);

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1003, "#update DIS\nstatus: blocked")), default);

        // They cannot even resolve it: a board they may not edit is not in their list, so
        // they are told it does not exist rather than that they are not allowed.
        reply.Outcome.Should().Be(TelegramOutcome.BoardNotResolved);
        (await _harness.Db.Boards.FirstAsync()).Status.Should().Be(BoardStatus.OnTrack);
    }

    [Fact]
    public async Task An_admin_update_lands_on_the_board_and_in_its_history()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1004, user);

        var reply = await Handler.Handle(new ProcessTelegramMessageCommand(
            From(1004, "#update DIS\nstatus: at risk\nprogress: 70\nsprint: Sprint 12")), default);

        reply.Outcome.Should().Be(TelegramOutcome.Applied);

        var board = await _harness.Db.Boards.FirstAsync();
        board.Status.Should().Be(BoardStatus.AtRisk);
        board.ProgressPercent.Should().Be(70);
        board.Sprint.Should().Be("Sprint 12");

        var audit = await _harness.Db.BoardAuditEntries.ToListAsync();
        audit.Should().HaveCount(3);
        audit.Should().OnlyContain(e => e.Source == "Telegram");
        audit.Should().OnlyContain(e => e.ChangedBy == "Shehan Cooray");
    }

    [Fact]
    public async Task The_audit_name_stays_the_persons_own_so_their_profile_still_finds_it()
    {
        // The person profile matches activity on the display name exactly. Decorating it
        // with "via Telegram" would quietly lose somebody their history.
        var user = SeedUser();
        SeedBoard();
        Link(1005, user);

        await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1005, "#update DIS\nprogress: 55")), default);

        var entry = await _harness.Db.BoardAuditEntries.FirstAsync();

        entry.ChangedBy.Should().Be("Shehan Cooray");
        entry.Summary.Should().Contain("(via Telegram)");
    }

    [Fact]
    public async Task Repeating_an_update_that_changes_nothing_writes_nothing()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1006, user);

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1006, "#update DIS\nprogress: 40")), default);

        reply.Outcome.Should().Be(TelegramOutcome.NoChange);
        (await _harness.Db.BoardAuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_dry_run_says_what_it_would_do_and_does_none_of_it()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1007, user);

        var reply = await Handler.Handle(new ProcessTelegramMessageCommand(
            From(1007, "#update DIS\nstatus: blocked"), DryRun: true), default);

        reply.Text.Should().StartWith("Would update");
        (await _harness.Db.Boards.FirstAsync()).Status.Should().Be(BoardStatus.OnTrack);
        (await _harness.Db.BoardAuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_free_note_becomes_the_blocker_note_when_the_board_is_being_blocked()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1008, user);

        await Handler.Handle(new ProcessTelegramMessageCommand(
            From(1008, "#update DIS\nstatus: blocked\nnote: waiting on MOH sign-off")), default);

        (await _harness.Db.Boards.FirstAsync()).BlockerNote.Should().Be("waiting on MOH sign-off");
    }

    [Fact]
    public async Task A_free_note_becomes_the_risk_note_when_a_risk_is_being_raised()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1009, user);

        await Handler.Handle(new ProcessTelegramMessageCommand(
            From(1009, "#update DIS\nrisk: high\nnote: the vendor has not confirmed")), default);

        var board = await _harness.Db.Boards.FirstAsync();
        board.RiskLevel.Should().Be(RiskLevel.High);
        board.RiskNote.Should().Be("the vendor has not confirmed");
    }

    [Fact]
    public async Task A_message_naming_no_board_gets_the_template_back()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1010, user);

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1010, "hello?")), default);

        // "hello?" has no key, so it is read as a board name — and no board matches it.
        reply.Outcome.Should().Be(TelegramOutcome.BoardNotResolved);
        (await _harness.Db.BoardAuditEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Slash_boards_lists_only_what_the_sender_may_edit()
    {
        var owner = SeedUser(UserRole.ProductOwner, "Pradeep S");
        SeedBoard("Discharge Revamp", "DIS", owner.Id);
        SeedBoard("Admission Revamp", "ADM");
        Link(1011, owner);

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1011, "/boards")), default);

        reply.Text.Should().Contain("DIS");
        reply.Text.Should().NotContain("ADM");
    }

    [Fact]
    public async Task An_enrolment_code_links_the_sender_and_cannot_be_used_twice()
    {
        var user = SeedUser();
        _harness.Db.TelegramEnrolments.Add(
            new TelegramEnrolment("ABCD2345", user.Id, "admin", TimeSpan.FromMinutes(30)));
        await _harness.Db.SaveChangesAsync();

        var first = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1012, "/start ABCD2345")), default);

        first.Outcome.Should().Be(TelegramOutcome.Command);
        first.Text.Should().Contain("Linked");
        (await _harness.Db.TelegramLinks.CountAsync()).Should().Be(1);

        var second = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(2020, "/start ABCD2345")), default);

        second.Outcome.Should().Be(TelegramOutcome.SenderNotLinked);
        (await _harness.Db.TelegramLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_expired_enrolment_code_is_refused()
    {
        var user = SeedUser();
        _harness.Db.TelegramEnrolments.Add(
            new TelegramEnrolment("EXPIRED1", user.Id, "admin", TimeSpan.FromMinutes(-1)));
        await _harness.Db.SaveChangesAsync();

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1013, "/start EXPIRED1")), default);

        reply.Outcome.Should().Be(TelegramOutcome.SenderNotLinked);
        (await _harness.Db.TelegramLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_chat_outside_the_allow_list_is_ignored_in_silence()
    {
        var user = SeedUser();
        SeedBoard();
        Link(1014, user);

        var settings = new TelegramSettings("https://api.telegram.org", "cipher", "hint", true,
            "admin");
        settings.ConfigureDelivery("777", replyToUnknownSenders: true);
        _harness.Db.TelegramSettings.Add(settings);
        await _harness.Db.SaveChangesAsync();

        var reply = await Handler.Handle(
            new ProcessTelegramMessageCommand(From(1014, "#update DIS\nstatus: blocked")), default);

        reply.Outcome.Should().Be(TelegramOutcome.ChatNotAllowed);
        reply.ShouldReply.Should().BeFalse();
        (await _harness.Db.Boards.FirstAsync()).Status.Should().Be(BoardStatus.OnTrack);
    }
}

public sealed class BoardCodeTests
{
    [Theory]
    [InlineData("Discharge Revamp - Gaps for MOH and S3 Rollout", "DISCHARGE")]
    [InlineData("Admission Revamp", "ADMISSION")]
    [InlineData("PHR Base Revamp (Covers: PHR IPD Revamp)", "PHR")]
    [InlineData("OPD UI Revamp - Summary View", "OPD")]
    // A first word too thin to identify anything takes the next one with it.
    [InlineData("AI Assistant", "AIASSISTANT")]
    [InlineData("", "BOARD")]
    public void Suggests_something_a_person_could_type_from_memory(string title, string expected)
    {
        Board.SuggestCode(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("dis")]
    [InlineData("DIS-4")]
    [InlineData("VIDA4")]
    public void Accepts_a_reasonable_code_and_stores_it_uppercase(string code)
    {
        var board = new Board("Discharge", "Discharge", "Aurora", null, BoardStatus.OnTrack, 0, "t");
        board.AssignCode(code);

        board.Code.Should().Be(code.ToUpperInvariant());
    }

    [Theory]
    [InlineData("D")]
    [InlineData("WAY-TOO-LONG-FOR-THIS")]
    [InlineData("DIS 4")]
    [InlineData("DIS/4")]
    public void Refuses_a_code_that_would_not_survive_being_typed(string code)
    {
        var board = new Board("Discharge", "Discharge", "Aurora", null, BoardStatus.OnTrack, 0, "t");

        var act = () => board.AssignCode(code);

        act.Should().Throw<DomainException>();
    }
}

public sealed class TelegramNoteRoutingTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_free_note_becomes_the_risk_note_when_the_status_is_at_risk()
    {
        // "At Risk" is a claim about risk, so the sentence explaining it belongs in the
        // risk note — not in the blocker note of a board that is not blocked.
        var user = new AppUser("a@pirt.example", "Shehan Cooray", UserRole.Admin, "hash");
        _harness.Db.Users.Add(user);

        var board = new Board("Discharge Revamp", "Discharge", "Aurora", null,
            BoardStatus.OnTrack, 10, "tester");
        board.AssignCode("DIS");
        _harness.Db.Boards.Add(board);
        _harness.Db.TelegramLinks.Add(new TelegramLink(4242, "s", "Shehan Cooray", user.Id));
        await _harness.Db.SaveChangesAsync();

        var handler = new ProcessTelegramMessageCommandHandler(_harness.Db, _harness.Notifier);

        await handler.Handle(new ProcessTelegramMessageCommand(
            new TelegramInboundMessage(1, 55, "private", 4242, "Shehan", "s",
                "#update DIS\nstatus: at risk\nnote: the vendor has not confirmed")), default);

        var saved = await _harness.Db.Boards.FirstAsync();
        saved.RiskNote.Should().Be("the vendor has not confirmed");
        saved.BlockerNote.Should().BeNull();
    }
}
