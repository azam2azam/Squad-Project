using Application.Abstractions;
using Application.Integrations;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Application.Tests;

/// <summary>
/// The Smartsheet connection holds a credential and can write to boards unattended, so
/// the same rules that are pinned for Jira are pinned here — plus the one that is unique
/// to a spreadsheet: sprint is never touched, because a sheet does not carry one.
/// </summary>
public sealed class SmartsheetIntegrationTests
{
    // ---------------------------------------------------------------------
    // Settings service
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Saved_token_is_encrypted_and_never_returned_in_clear()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        var view = await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "SMART-super-secret-WXYZ",
            Enabled: true, AutoApply: false, SyncIntervalMinutes: 30,
            ProgressColumn: null, StatusColumn: null));

        var stored = harness.Db.SmartsheetSettings.Single();

        stored.EncryptedAccessToken.Should().NotContain("SMART-super-secret-WXYZ");
        view.TokenHint.Should().EndWith("WXYZ").And.NotContain("super-secret");

        var credentials = await service.GetCredentialsAsync();
        credentials!.AccessToken.Should().Be("SMART-super-secret-WXYZ");
    }

    [Fact]
    public async Task Blank_token_on_a_later_save_keeps_the_stored_one()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "first-token-ABCD", true, false, 30, null, null));

        // The UI is never given the token, so it cannot send it back. A blank field must
        // mean "leave it alone" — not "wipe the working connection".
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", AccessToken: "", Enabled: true,
            AutoApply: true, SyncIntervalMinutes: 60,
            ProgressColumn: "Percent Done", StatusColumn: "State"));

        var credentials = await service.GetCredentialsAsync();
        credentials!.AccessToken.Should().Be("first-token-ABCD");
        credentials.ProgressColumn.Should().Be("Percent Done");

        var view = await service.GetAsync();
        view.SyncIntervalMinutes.Should().Be(60);
        view.AutoApply.Should().BeTrue();
    }

    [Fact]
    public async Task Configuration_wins_over_anything_saved_in_the_database()
    {
        using var harness = new TestHarness();

        var service = BuildService(harness, new Dictionary<string, string?>
        {
            ["Smartsheet:Enabled"] = "true",
            ["Smartsheet:AccessToken"] = "pinned-token",
        });

        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://from-the-ui.example", "ui-token", true, false, 30, null, null));

        var credentials = await service.GetCredentialsAsync();
        credentials!.AccessToken.Should().Be("pinned-token");

        (await service.GetAsync()).OverriddenByConfiguration.Should().BeTrue();
    }

    [Fact]
    public async Task Blank_column_names_fall_back_to_the_conventional_titles()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, false, 30,
            ProgressColumn: "  ", StatusColumn: null));

        var credentials = await service.GetCredentialsAsync();

        // Reading nothing would be worse than reading the column most templates use.
        credentials!.ProgressColumn.Should().Be(SmartsheetSettings.DefaultProgressColumn);
        credentials.StatusColumn.Should().Be(SmartsheetSettings.DefaultStatusColumn);
    }

    // ---------------------------------------------------------------------
    // The URL rule
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("http://smartsheet.internal.acme.com")]
    [InlineData("http://10.0.0.5:8080")]
    public void Http_to_a_remote_host_is_refused(string url)
    {
        var act = () => new SmartsheetSettings(url, "cipher", "hint", true, "admin");

        act.Should().Throw<DomainException>().WithMessage("*https*");
    }

    [Theory]
    [InlineData("https://api.smartsheet.com/2.0")]
    [InlineData("http://127.0.0.1:5402")]
    public void Https_anywhere_and_http_on_loopback_are_allowed(string url)
    {
        var settings = new SmartsheetSettings(url, "cipher", "hint", true, "admin");

        settings.BaseUrl.Should().Be(url);
    }

    [Fact]
    public void A_blank_url_falls_back_to_the_public_api()
    {
        // Most deployments never change this, so an empty box should not be an error.
        var settings = new SmartsheetSettings("", "cipher", "hint", true, "admin");

        settings.BaseUrl.Should().Be(SmartsheetSettings.DefaultBaseUrl);
    }

    // ---------------------------------------------------------------------
    // The sync handler
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Scheduled_sync_declines_to_write_while_auto_apply_is_off()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token",
            Enabled: true, AutoApply: false, SyncIntervalMinutes: 30, null, null));

        var board = SeedLinkedBoard(harness);

        var report = await Handler(harness, service, new StubSmartsheetClient()).Handle(
            new SyncBoardsFromSmartsheetCommand(RespectAutoApply: true), default);

        report.Ran.Should().BeFalse();
        report.Message.Should().Contain("Auto-apply is off");
        harness.Db.Boards.Single(b => b.Id == board.Id).ProgressPercent.Should().Be(10);
    }

    [Fact]
    public async Task Admin_pressing_sync_now_writes_even_when_auto_apply_is_off()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, false, 30, null, null));

        var board = SeedLinkedBoard(harness);

        var report = await Handler(harness, service, new StubSmartsheetClient()).Handle(
            new SyncBoardsFromSmartsheetCommand("Nadia Al-Harbi", RespectAutoApply: false), default);

        report.BoardsUpdated.Should().Be(1);
        harness.Db.Boards.Single(b => b.Id == board.Id).ProgressPercent.Should().Be(52);
        harness.Db.BoardAuditEntries.Should().OnlyContain(e => e.ChangedBy == "Nadia Al-Harbi");
    }

    [Fact]
    public async Task A_sync_never_touches_the_sprint()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, true, 30, null, null));

        var board = SeedLinkedBoard(harness);

        await Handler(harness, service, new StubSmartsheetClient()).Handle(
            new SyncBoardsFromSmartsheetCommand(), default);

        // A sheet carries no sprint. Blanking the Product Owner's value would lose
        // information the integration never had in the first place.
        harness.Db.Boards.Single(b => b.Id == board.Id).Sprint.Should().Be("Q3");
    }

    [Fact]
    public async Task An_unreachable_sheet_does_not_stop_the_others()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, true, 30, null, null));

        SeedLinkedBoard(harness, "9999", "Legacy Migration");
        var healthy = SeedLinkedBoard(harness, "1234", "Patient Portal");

        var report = await Handler(harness, service, new StubSmartsheetClient(failFor: "9999"))
            .Handle(new SyncBoardsFromSmartsheetCommand(), default);

        report.BoardsUnreachable.Should().Be(1);
        report.BoardsUpdated.Should().Be(1);
        harness.Db.Boards.Single(b => b.Id == healthy.Id).ProgressPercent.Should().Be(52);
    }

    [Fact]
    public async Task Only_boards_that_changed_are_announced_to_viewers()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, true, 30, null, null));

        SeedLinkedBoard(harness);

        var handler = Handler(harness, service, new StubSmartsheetClient());
        await handler.Handle(new SyncBoardsFromSmartsheetCommand(), default);
        harness.Notifier.BoardUpdates.Should().HaveCount(1);

        // Second run changes nothing, so nothing should be broadcast.
        await handler.Handle(new SyncBoardsFromSmartsheetCommand(), default);
        harness.Notifier.BoardUpdates.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_board_with_no_sheet_id_is_left_alone()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveSmartsheetSettings(
            "https://api.smartsheet.com/2.0", "token", true, true, 30, null, null));

        var board = new Board("Unlinked", "Portal", "Aurora", "Q3", BoardStatus.OnTrack, 10, "tester");
        harness.Db.Boards.Add(board);
        await harness.Db.SaveChangesAsync();

        var report = await Handler(harness, service, new StubSmartsheetClient())
            .Handle(new SyncBoardsFromSmartsheetCommand(), default);

        report.BoardsConsidered.Should().Be(0);
        harness.Db.Boards.Single(b => b.Id == board.Id).ProgressPercent.Should().Be(10);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static SmartsheetSettingsService BuildService(
        TestHarness harness, Dictionary<string, string?>? configuration = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
            .Build();

        return new SmartsheetSettingsService(
            harness.Db,
            new EphemeralDataProtectionProvider(),
            config,
            harness.CurrentUser,
            NullLogger<SmartsheetSettingsService>.Instance);
    }

    private static SyncBoardsFromSmartsheetCommandHandler Handler(
        TestHarness harness, ISmartsheetSettingsService settings, ISmartsheetClient client) =>
        new(harness.Db, client, settings, harness.Notifier,
            NullLogger<SyncBoardsFromSmartsheetCommandHandler>.Instance);

    private static Board SeedLinkedBoard(
        TestHarness harness, string sheetId = "1234", string title = "Patient Portal")
    {
        var board = new Board(title, "Portal", "Aurora", "Q3", BoardStatus.OnTrack, 10, "tester");
        board.LinkSmartsheet(sheetId);

        harness.Db.Boards.Add(board);
        harness.Db.SaveChanges();
        return board;
    }

    /// <summary>Returns a fixed snapshot, so the tests assert on our logic, not Smartsheet's.</summary>
    private sealed class StubSmartsheetClient(string? failFor = null) : ISmartsheetClient
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<SmartsheetSnapshot?> GetSnapshotAsync(
            string sheetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(sheetId == failFor
                ? null
                : new SmartsheetSnapshot("Delivery Plan", 2, 5, 1, 52, true,
                    BoardStatus.Blocked, "1 of 5 rows are blocked."));
    }
}
