using Application.Abstractions;
using Application.Messaging;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// Team messaging.
///
/// The rules worth pinning are the ones that would be embarrassing to get wrong: a direct
/// thread staying private even from an administrator, one conversation per squad however
/// the name is typed, and unread counting from the moment somebody joined rather than
/// from the beginning of time.
/// </summary>
public sealed class MessagingTests : IDisposable
{
    private readonly TestHarness _harness = new();
    private readonly RecordingMessageNotifier _notifier = new();

    public void Dispose() => _harness.Dispose();

    // -- fixtures -----------------------------------------------------------

    private AppUser SeedUser(string name, UserRole role = UserRole.ProductOwner,
        Guid? personId = null)
    {
        var user = new AppUser($"{Guid.NewGuid():N}@pirt.example", name, role, "hash");
        if (personId is { } id) user.LinkToPerson(id);

        _harness.Db.Users.Add(user);
        _harness.Db.SaveChanges();
        return user;
    }

    private Board SeedBoard(string title = "Discharge Revamp", string squadName = "Nanditha")
    {
        var board = new Board(title, "VIDA HIS", squadName, "Sprint 9",
            BoardStatus.OnTrack, 40, "seed");

        _harness.Db.Boards.Add(board);
        _harness.Db.SaveChanges();
        return board;
    }

    private void SignedInAs(AppUser user) => _harness.AsRole(user.Role, user.Id);

    private OpenBoardConversationCommandHandler OpenBoard =>
        new(_harness.Db, _harness.UserContext);

    private OpenSquadConversationCommandHandler OpenSquad =>
        new(_harness.Db, _harness.UserContext);

    private OpenDirectConversationCommandHandler OpenDirect =>
        new(_harness.Db, _harness.UserContext);

    private PostMessageCommandHandler Post =>
        new(_harness.Db, _harness.UserContext, _notifier);

    private EditMessageCommandHandler EditMessage =>
        new(_harness.Db, _harness.UserContext, _notifier);

    private DeleteMessageCommandHandler DeleteMessage =>
        new(_harness.Db, _harness.UserContext, _notifier);

    private GetInboxQueryHandler Inbox => new(_harness.Db, _harness.UserContext);

    private GetConversationQueryHandler Thread => new(_harness.Db, _harness.UserContext);

    private GetUnreadSummaryQueryHandler Unread => new(_harness.Db, _harness.UserContext);

    private MarkConversationReadCommandHandler MarkRead =>
        new(_harness.Db, _harness.UserContext);

    // -- opening ------------------------------------------------------------

    [Fact]
    public async Task Opening_a_board_channel_twice_returns_the_same_conversation()
    {
        var user = SeedUser("Nadia Al-Harbi");
        SignedInAs(user);
        var board = SeedBoard();

        var first = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        var second = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        second.Should().Be(first);
        (await _harness.Db.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_squad_channel_spans_every_board_that_squad_runs()
    {
        var user = SeedUser("Nadia Al-Harbi");
        SignedInAs(user);

        SeedBoard("Discharge Revamp", "Nanditha");
        SeedBoard("Pharmacy Rework", "Nanditha");

        var id = await OpenSquad.Handle(new OpenSquadConversationCommand("Nanditha"), default);

        var thread = await Thread.Handle(new GetConversationQuery(id), default);

        thread.Kind.Should().Be(ConversationKind.Squad);
        thread.RelatedBoards.Should().HaveCount(2);
        thread.Subtitle.Should().Be("2 boards");
    }

    [Fact]
    public async Task A_squad_name_typed_differently_resolves_to_one_conversation()
    {
        var user = SeedUser("Nadia Al-Harbi");
        SignedInAs(user);
        SeedBoard("Claims", "Pradeep & Shehan");

        var first = await OpenSquad.Handle(
            new OpenSquadConversationCommand("Pradeep & Shehan"), default);

        // Same squad, typed by somebody else on a different day.
        var second = await OpenSquad.Handle(
            new OpenSquadConversationCommand("  pradeep &   shehan "), default);

        second.Should().Be(first);
    }

    [Fact]
    public async Task A_squad_nobody_runs_is_refused()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        SeedBoard("Claims", "Nanditha");

        var open = async () => await OpenSquad.Handle(
            new OpenSquadConversationCommand("Typo Squad"), default);

        await open.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task A_direct_conversation_is_the_same_thread_from_either_side()
    {
        var alice = SeedUser("Nadia Al-Harbi");
        var bob = SeedUser("Shehan Cooray");

        SignedInAs(alice);
        var fromAlice = await OpenDirect.Handle(new OpenDirectConversationCommand(bob.Id), default);

        SignedInAs(bob);
        var fromBob = await OpenDirect.Handle(new OpenDirectConversationCommand(alice.Id), default);

        fromBob.Should().Be(fromAlice);
        (await _harness.Db.Conversations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task You_cannot_start_a_conversation_with_yourself()
    {
        var user = SeedUser("Nadia Al-Harbi");
        SignedInAs(user);

        var open = async () => await OpenDirect.Handle(
            new OpenDirectConversationCommand(user.Id), default);

        await open.Should().ThrowAsync<ForbiddenException>();
    }

    // -- privacy ------------------------------------------------------------

    [Fact]
    public async Task An_administrator_cannot_read_two_other_peoples_direct_thread()
    {
        var alice = SeedUser("Nadia Al-Harbi");
        var bob = SeedUser("Shehan Cooray");

        SignedInAs(alice);
        var id = await OpenDirect.Handle(new OpenDirectConversationCommand(bob.Id), default);
        await Post.Handle(new PostMessageCommand(id, "Between us."), default);

        // The most senior account in the system, and it still does not open.
        var admin = SeedUser("Administrator", UserRole.Admin);
        SignedInAs(admin);

        var read = async () => await Thread.Handle(new GetConversationQuery(id), default);

        await read.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_board_channel_is_readable_by_anyone_signed_in()
    {
        var author = SeedUser("Nadia Al-Harbi");
        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        await Post.Handle(new PostMessageCommand(id, "Slipping a week."), default);

        // A Viewer — the least privileged role there is.
        SignedInAs(SeedUser("Executive Viewer", UserRole.Viewer));

        var thread = await Thread.Handle(new GetConversationQuery(id), default);

        thread.Messages.Should().ContainSingle()
            .Which.Body.Should().Be("Slipping a week.");
    }

    // -- writing ------------------------------------------------------------

    [Fact]
    public async Task Posting_broadcasts_the_message()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(new PostMessageCommand(id, "Theatre slots confirmed."), default);

        _notifier.Posted.Should().ContainSingle()
            .Which.ConversationId.Should().Be(id);
    }

    [Fact]
    public async Task Posting_updates_the_inbox_preview()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(new PostMessageCommand(id, "  Theatre  slots\nconfirmed.  "), default);

        var inbox = await Inbox.Handle(new GetInboxQuery(), default);

        // Whitespace is collapsed: the inbox has one line to work with.
        inbox.Should().ContainSingle()
            .Which.LastMessagePreview.Should().Be("Theatre slots confirmed.");
    }

    [Fact]
    public async Task Only_the_author_may_edit_a_message()
    {
        var author = SeedUser("Nadia Al-Harbi");
        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        var posted = await Post.Handle(new PostMessageCommand(id, "Original."), default);

        // Not even an administrator: editing somebody's words under their name is forgery.
        SignedInAs(SeedUser("Administrator", UserRole.Admin));

        var edit = async () => await EditMessage.Handle(
            new EditMessageCommand(posted.Id, "Rewritten."), default);

        await edit.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task An_edited_message_says_so()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        var posted = await Post.Handle(new PostMessageCommand(id, "Original."), default);

        var edited = await EditMessage.Handle(
            new EditMessageCommand(posted.Id, "Corrected."), default);

        edited.Body.Should().Be("Corrected.");
        edited.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_withdrawn_message_leaves_a_tombstone_and_keeps_no_body()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        var posted = await Post.Handle(new PostMessageCommand(id, "Sent in error."), default);

        var deleted = await DeleteMessage.Handle(new DeleteMessageCommand(posted.Id), default);

        deleted.IsDeleted.Should().BeTrue();
        deleted.Body.Should().BeEmpty();

        // Cleared in the database too — a body that still exists still leaks.
        var row = await _harness.Db.Messages.AsNoTracking()
            .FirstAsync(m => m.Id == posted.Id);

        row.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task An_administrator_may_moderate_a_channel()
    {
        var author = SeedUser("Nadia Al-Harbi");
        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        var posted = await Post.Handle(new PostMessageCommand(id, "Off topic."), default);

        SignedInAs(SeedUser("Administrator", UserRole.Admin));

        var deleted = await DeleteMessage.Handle(new DeleteMessageCommand(posted.Id), default);

        deleted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_reply_must_stay_inside_its_own_conversation()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var first = SeedBoard("Discharge Revamp", "Nanditha");
        var second = SeedBoard("Pharmacy Rework", "Udith");

        var a = await OpenBoard.Handle(new OpenBoardConversationCommand(first.Id), default);
        var b = await OpenBoard.Handle(new OpenBoardConversationCommand(second.Id), default);

        var posted = await Post.Handle(new PostMessageCommand(a, "In the first."), default);

        var reply = async () => await Post.Handle(
            new PostMessageCommand(b, "Answering across threads.", posted.Id), default);

        await reply.Should().ThrowAsync<DomainException>();
    }

    // -- mentions -----------------------------------------------------------

    [Fact]
    public async Task A_mention_resolves_to_the_longest_matching_name()
    {
        var person = new Person("Sara Al-Otaibi", Role.Developer);
        _harness.Db.People.Add(person);
        await _harness.Db.SaveChangesAsync();

        var sara = SeedUser("sara.alotaibi", UserRole.ProductOwner, person.Id);
        var author = SeedUser("Nadia Al-Harbi");

        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(
            new PostMessageCommand(id, "@Sara Al-Otaibi can you confirm the slot?"), default);

        var mentions = await _harness.Db.MessageMentions.AsNoTracking().ToListAsync();

        mentions.Should().ContainSingle()
            .Which.MentionedUserId.Should().Be(sara.Id);
    }

    [Fact]
    public async Task A_mention_of_nobody_is_simply_text()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(new PostMessageCommand(id, "Email me @ the usual address"), default);

        (await _harness.Db.MessageMentions.CountAsync()).Should().Be(0);
    }

    // -- unread -------------------------------------------------------------

    [Fact]
    public async Task Your_own_messages_are_never_unread_to_you()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(new PostMessageCommand(id, "Noted."), default);

        var summary = await Unread.Handle(new GetUnreadSummaryQuery(), default);

        summary.Messages.Should().Be(0);
    }

    [Fact]
    public async Task Joining_a_channel_does_not_make_its_history_unread()
    {
        var author = SeedUser("Nadia Al-Harbi");
        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        await Post.Handle(new PostMessageCommand(id, "Long ago."), default);
        await Post.Handle(new PostMessageCommand(id, "Also long ago."), default);

        // Somebody new opens the channel for the first time today.
        SignedInAs(SeedUser("Shehan Cooray"));
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        var summary = await Unread.Handle(new GetUnreadSummaryQuery(), default);

        // Telling a newcomer they have missed everything is how a channel gets muted.
        summary.Messages.Should().Be(0);
    }

    [Fact]
    public async Task A_message_from_someone_else_counts_as_unread()
    {
        var author = SeedUser("Nadia Al-Harbi");
        var reader = SeedUser("Shehan Cooray");

        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(reader);
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        await MarkRead.Handle(new MarkConversationReadCommand(id), default);

        SignedInAs(author);
        await Post.Handle(new PostMessageCommand(id, "One for you."), default);

        SignedInAs(reader);
        var summary = await Unread.Handle(new GetUnreadSummaryQuery(), default);

        summary.Messages.Should().Be(1);
        summary.Conversations.Should().Be(1);
    }

    [Fact]
    public async Task Reading_a_conversation_clears_its_unread_count()
    {
        var author = SeedUser("Nadia Al-Harbi");
        var reader = SeedUser("Shehan Cooray");

        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(reader);
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(author);
        await Post.Handle(new PostMessageCommand(id, "One for you."), default);

        SignedInAs(reader);
        await MarkRead.Handle(new MarkConversationReadCommand(id), default);

        (await Unread.Handle(new GetUnreadSummaryQuery(), default)).Messages.Should().Be(0);
    }

    [Fact]
    public async Task Read_state_only_moves_forwards()
    {
        var author = SeedUser("Nadia Al-Harbi");
        var reader = SeedUser("Shehan Cooray");

        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(reader);
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        await MarkRead.Handle(new MarkConversationReadCommand(id), default);

        var membership = await _harness.Db.ConversationMembers
            .FirstAsync(m => m.ConversationId == id && m.UserId == reader.Id);

        var readAt = membership.LastReadAt;

        // A stale second tab reporting an older timestamp must not un-read anything.
        membership.MarkReadAt(DateTimeOffset.UtcNow.AddHours(-2));

        membership.LastReadAt.Should().Be(readAt);
    }

    [Fact]
    public async Task A_muted_channel_stops_counting_but_a_mention_still_does()
    {
        var person = new Person("Shehan Cooray", Role.Developer);
        _harness.Db.People.Add(person);
        await _harness.Db.SaveChangesAsync();

        var author = SeedUser("Nadia Al-Harbi");
        var reader = SeedUser("Shehan Cooray", UserRole.ProductOwner, person.Id);

        SignedInAs(author);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(reader);
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        await new SetConversationMutedCommandHandler(_harness.Db, _harness.UserContext)
            .Handle(new SetConversationMutedCommand(id, true), default);

        SignedInAs(author);
        await Post.Handle(new PostMessageCommand(id, "General chatter."), default);

        SignedInAs(reader);
        (await Unread.Handle(new GetUnreadSummaryQuery(), default)).Messages.Should().Be(0);

        SignedInAs(author);
        await Post.Handle(new PostMessageCommand(id, "@Shehan Cooray can you look?"), default);

        SignedInAs(reader);
        var summary = await Unread.Handle(new GetUnreadSummaryQuery(), default);

        // Muting silences the chatter, not somebody asking you directly.
        summary.Mentions.Should().Be(1);
        summary.Messages.Should().Be(0);
    }

    // -- the inbox ----------------------------------------------------------

    [Fact]
    public async Task The_inbox_only_lists_conversations_you_have_joined()
    {
        var other = SeedUser("Nadia Al-Harbi");
        SignedInAs(other);
        var board = SeedBoard();
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        // Somebody who has never opened that channel.
        SignedInAs(SeedUser("Shehan Cooray"));

        (await Inbox.Handle(new GetInboxQuery(), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_direct_thread_is_titled_after_the_other_person()
    {
        var person = new Person("Shehan Cooray", Role.Developer);
        _harness.Db.People.Add(person);
        await _harness.Db.SaveChangesAsync();

        var alice = SeedUser("Nadia Al-Harbi");
        var bob = SeedUser("s.cooray", UserRole.ProductOwner, person.Id);

        SignedInAs(alice);
        var id = await OpenDirect.Handle(new OpenDirectConversationCommand(bob.Id), default);
        await Post.Handle(new PostMessageCommand(id, "Morning."), default);

        var forAlice = await Inbox.Handle(new GetInboxQuery(), default);

        // The roster name, not the account's login-ish display name.
        forAlice.Should().ContainSingle().Which.Title.Should().Be("Shehan Cooray");

        SignedInAs(bob);
        var forBob = await Inbox.Handle(new GetInboxQuery(), default);

        forBob.Should().ContainSingle().Which.Title.Should().Be("Nadia Al-Harbi");
    }

    [Fact]
    public async Task A_board_thread_lists_the_squad_including_people_with_no_account()
    {
        var withAccount = new Person("Shehan Cooray", Role.Developer);
        var withoutAccount = new Person("Udith Perera", Role.QaEngineer);
        _harness.Db.People.AddRange(withAccount, withoutAccount);
        await _harness.Db.SaveChangesAsync();

        SeedUser("s.cooray", UserRole.ProductOwner, withAccount.Id);

        var board = SeedBoard();

        // Added to the set explicitly: a child reached through a tracked parent is marked
        // Modified rather than Added, and EF then updates a row that does not exist.
        _harness.Db.SquadMembers.Add(board.AddMember(withAccount, Role.Developer));
        _harness.Db.SquadMembers.Add(board.AddMember(withoutAccount, Role.QaEngineer));
        await _harness.Db.SaveChangesAsync();

        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        var thread = await Thread.Handle(new GetConversationQuery(id), default);

        thread.Participants.Should().HaveCount(2);

        // Shown but flagged: a mention will not reach somebody who cannot sign in, and
        // hiding them would misrepresent the squad as smaller than it is.
        thread.Participants.Should().ContainSingle(p => !p.HasAccount)
            .Which.DisplayName.Should().Be("Udith Perera");
    }

    [Fact]
    public async Task Leaving_a_channel_removes_it_from_your_inbox_only()
    {
        var stayer = SeedUser("Nadia Al-Harbi");
        var leaver = SeedUser("Shehan Cooray");

        SignedInAs(stayer);
        var board = SeedBoard();
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        SignedInAs(leaver);
        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);
        await new LeaveConversationCommandHandler(_harness.Db, _harness.UserContext)
            .Handle(new LeaveConversationCommand(id), default);

        (await Inbox.Handle(new GetInboxQuery(), default)).Should().BeEmpty();

        SignedInAs(stayer);
        (await Inbox.Handle(new GetInboxQuery(), default)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_direct_conversation_cannot_be_left()
    {
        var alice = SeedUser("Nadia Al-Harbi");
        var bob = SeedUser("Shehan Cooray");

        SignedInAs(alice);
        var id = await OpenDirect.Handle(new OpenDirectConversationCommand(bob.Id), default);

        var leave = async () => await new LeaveConversationCommandHandler(
                _harness.Db, _harness.UserContext)
            .Handle(new LeaveConversationCommand(id), default);

        await leave.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task A_renamed_board_renames_its_channel()
    {
        SignedInAs(SeedUser("Nadia Al-Harbi"));
        var board = SeedBoard("Discharge Revamp");
        var id = await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        board.UpdateMeta("Discharge Revamp — Phase 2", "VIDA HIS", "Nanditha", "Sprint 10",
            BoardStatus.OnTrack, 55, null, null, null, null, null);
        await _harness.Db.SaveChangesAsync();

        await OpenBoard.Handle(new OpenBoardConversationCommand(board.Id), default);

        var thread = await Thread.Handle(new GetConversationQuery(id), default);

        // A channel still carrying last month's title is one people stop trusting.
        thread.Title.Should().Be("Discharge Revamp — Phase 2");
    }
}
