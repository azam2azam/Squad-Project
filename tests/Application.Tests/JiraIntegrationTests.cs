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
/// The Jira connection holds a credential and can write to boards unattended, so the
/// rules that keep it safe are pinned here rather than left to review.
/// </summary>
public sealed class JiraIntegrationTests
{
    // ---------------------------------------------------------------------
    // Settings service
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Saved_token_is_encrypted_and_never_returned_in_clear()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        var view = await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "board@acme.com", "ATATT-super-secret-WXYZ",
            Enabled: true, AutoApply: false, SyncIntervalMinutes: 30));

        var stored = harness.Db.JiraSettings.Single();

        stored.EncryptedApiToken.Should().NotContain("ATATT-super-secret-WXYZ");
        view.TokenHint.Should().EndWith("WXYZ").And.NotContain("super-secret");

        // The credential the client actually uses must still round-trip.
        var credentials = await service.GetCredentialsAsync();
        credentials!.ApiToken.Should().Be("ATATT-super-secret-WXYZ");
    }

    [Fact]
    public async Task Blank_token_on_a_later_save_keeps_the_stored_one()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "board@acme.com", "first-token-ABCD",
            true, false, 30));

        // The UI is never given the token, so it cannot send it back. A blank field must
        // mean "leave it alone" — not "wipe the working connection".
        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "board@acme.com", ApiToken: "",
            Enabled: true, AutoApply: true, SyncIntervalMinutes: 60));

        var credentials = await service.GetCredentialsAsync();
        credentials!.ApiToken.Should().Be("first-token-ABCD");

        var view = await service.GetAsync();
        view.SyncIntervalMinutes.Should().Be(60);
        view.AutoApply.Should().BeTrue();
    }

    [Fact]
    public async Task Configuration_wins_over_anything_saved_in_the_database()
    {
        using var harness = new TestHarness();

        // A locked-down deployment pins the credentials outside an app admin's reach.
        var service = BuildService(harness, new Dictionary<string, string?>
        {
            ["Jira:Enabled"] = "true",
            ["Jira:BaseUrl"] = "https://pinned.atlassian.net",
            ["Jira:Email"] = "pinned@acme.com",
            ["Jira:ApiToken"] = "pinned-token",
        });

        await service.SaveAsync(new SaveJiraSettings(
            "https://from-the-ui.atlassian.net", "ui@acme.com", "ui-token", true, false, 30));

        var credentials = await service.GetCredentialsAsync();
        credentials!.BaseUrl.Should().Be("https://pinned.atlassian.net");
        credentials.ApiToken.Should().Be("pinned-token");

        var view = await service.GetAsync();
        view.OverriddenByConfiguration.Should().BeTrue();
    }

    [Fact]
    public async Task Clearing_removes_the_stored_credential_entirely()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);

        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "board@acme.com", "token", true, false, 30));
        await service.ClearAsync();

        harness.Db.JiraSettings.Should().BeEmpty();
        (await service.GetCredentialsAsync()).Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // The URL rule
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("http://jira.internal.acme.com")]
    [InlineData("http://10.0.0.5:8080")]
    public void Http_to_a_remote_host_is_refused(string url)
    {
        var act = () => new JiraSettings(url, "a@b.com", "cipher", "hint", true, "admin");

        act.Should().Throw<DomainException>()
            .WithMessage("*https*");
    }

    [Theory]
    [InlineData("https://acme.atlassian.net")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:5399")]
    public void Https_anywhere_and_http_on_loopback_are_allowed(string url)
    {
        // Loopback is allowed so a self-hosted Jira on the same box can be used in a test
        // environment; nothing leaves the machine, so there is nothing to intercept.
        var settings = new JiraSettings(url, "a@b.com", "cipher", "hint", true, "admin");

        settings.BaseUrl.Should().Be(url);
    }

    // ---------------------------------------------------------------------
    // Applying a snapshot to a board
    // ---------------------------------------------------------------------

    [Fact]
    public void Applying_a_snapshot_never_touches_what_a_human_wrote()
    {
        var board = new Board("Patient Portal", "Portal", "Aurora", "Sprint 1",
            BoardStatus.OnTrack, 10, "tester");
        board.UpdateMeta("Patient Portal", "Portal", "Aurora", "Sprint 1",
            BoardStatus.OnTrack, 10, "Waiting on infosec sign-off", null, null, "PIRT", null,
            RiskLevel.High, "Vendor may slip");

        board.ApplyJiraSnapshot("Sprint 2", 60, BoardStatus.Blocked);

        board.Sprint.Should().Be("Sprint 2");
        board.ProgressPercent.Should().Be(60);
        board.Status.Should().Be(BoardStatus.Blocked);

        // The Product Owner's narrative and risk assessment are theirs, not Jira's.
        board.BlockerNote.Should().Be("Waiting on infosec sign-off");
        board.RiskLevel.Should().Be(RiskLevel.High);
        board.RiskNote.Should().Be("Vendor may slip");
    }

    [Fact]
    public void Applying_an_unchanged_snapshot_reports_no_changes()
    {
        var board = new Board("Portal", "Portal", "Aurora", "Sprint 2",
            BoardStatus.Blocked, 60, "tester");

        var changes = board.ApplyJiraSnapshot("Sprint 2", 60, BoardStatus.Blocked);

        // Otherwise every sync interval would write a fresh page of audit entries saying
        // nothing happened.
        changes.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_sprint_does_not_erase_the_one_already_recorded()
    {
        var board = new Board("Portal", "Portal", "Aurora", "Sprint 7",
            BoardStatus.OnTrack, 20, "tester");

        // Sprint lives in a custom field whose id differs per Jira site. When it cannot be
        // read, that is ignorance, not evidence the board has no sprint.
        var changes = board.ApplyJiraSnapshot(null, 20, BoardStatus.OnTrack);

        changes.Should().BeEmpty();
        board.Sprint.Should().Be("Sprint 7");
    }

    // ---------------------------------------------------------------------
    // The sync handler
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Scheduled_sync_declines_to_write_while_auto_apply_is_off()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "a@b.com", "token",
            Enabled: true, AutoApply: false, SyncIntervalMinutes: 30));

        var board = SeedLinkedBoard(harness);

        var report = await Handler(harness, service, new StubJiraClient()).Handle(
            new SyncBoardsFromJiraCommand(RespectAutoApply: true), default);

        report.Ran.Should().BeFalse();
        report.Message.Should().Contain("Auto-apply is off");
        harness.Db.Boards.Single(b => b.Id == board.Id).ProgressPercent.Should().Be(10);
    }

    [Fact]
    public async Task Admin_pressing_sync_now_writes_even_when_auto_apply_is_off()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "a@b.com", "token", true, false, 30));

        var board = SeedLinkedBoard(harness);

        var report = await Handler(harness, service, new StubJiraClient()).Handle(
            new SyncBoardsFromJiraCommand("Nadia Al-Harbi", RespectAutoApply: false), default);

        report.BoardsUpdated.Should().Be(1);
        harness.Db.Boards.Single(b => b.Id == board.Id).ProgressPercent.Should().Be(60);

        // The change is attributable to the person who asked for it.
        harness.Db.BoardAuditEntries.Should().OnlyContain(e => e.ChangedBy == "Nadia Al-Harbi");
    }

    [Fact]
    public async Task An_unreachable_project_does_not_stop_the_others()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "a@b.com", "token", true, true, 30));

        SeedLinkedBoard(harness, "DEAD", "Legacy Migration");
        var healthy = SeedLinkedBoard(harness, "PIRT", "Patient Portal");

        // The stub returns nothing for DEAD, standing in for a project the account cannot
        // read or a Jira outage mid-run.
        var client = new StubJiraClient(failFor: "DEAD");

        var report = await Handler(harness, service, client).Handle(
            new SyncBoardsFromJiraCommand(), default);

        report.BoardsUnreachable.Should().Be(1);
        report.BoardsUpdated.Should().Be(1);
        harness.Db.Boards.Single(b => b.Id == healthy.Id).ProgressPercent.Should().Be(60);
    }

    [Fact]
    public async Task Only_boards_that_changed_are_announced_to_viewers()
    {
        using var harness = new TestHarness();
        var service = BuildService(harness);
        await service.SaveAsync(new SaveJiraSettings(
            "https://acme.atlassian.net", "a@b.com", "token", true, true, 30));

        SeedLinkedBoard(harness);

        var handler = Handler(harness, service, new StubJiraClient());
        await handler.Handle(new SyncBoardsFromJiraCommand(), default);
        harness.Notifier.BoardUpdates.Should().HaveCount(1);

        // Second run changes nothing, so nothing should be broadcast — otherwise every
        // open editor would flash a "board updated" banner on every interval.
        await handler.Handle(new SyncBoardsFromJiraCommand(), default);
        harness.Notifier.BoardUpdates.Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static JiraSettingsService BuildService(
        TestHarness harness, Dictionary<string, string?>? configuration = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
            .Build();

        return new JiraSettingsService(
            harness.Db,
            new EphemeralDataProtectionProvider(),
            config,
            harness.CurrentUser,
            NullLogger<JiraSettingsService>.Instance);
    }

    private static SyncBoardsFromJiraCommandHandler Handler(
        TestHarness harness, IJiraSettingsService settings, IJiraClient client) =>
        new(harness.Db, client, settings, harness.Notifier,
            NullLogger<SyncBoardsFromJiraCommandHandler>.Instance);

    private static Board SeedLinkedBoard(
        TestHarness harness, string projectKey = "PIRT", string title = "Patient Portal")
    {
        var board = new Board(title, "Portal", "Aurora", "Sprint 1", BoardStatus.OnTrack, 10, "tester");
        board.UpdateMeta(title, "Portal", "Aurora", "Sprint 1",
            BoardStatus.OnTrack, 10, null, null, null, projectKey, null);

        harness.Db.Boards.Add(board);
        harness.Db.SaveChanges();
        return board;
    }

    /// <summary>Returns a fixed snapshot, so the tests assert on our logic, not Jira's.</summary>
    private sealed class StubJiraClient(string? failFor = null) : IJiraClient
    {
        public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<JiraSnapshot?> GetSnapshotAsync(string projectKey, string? boardId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(projectKey == failFor
                ? null
                : new JiraSnapshot("Sprint 2", 3, 5, 1, 60, BoardStatus.Blocked,
                    "1 of 5 issues are blocked."));
    }
}
