using Application.Staff;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// The capacity arithmetic is the part of this feature most likely to be quietly wrong,
/// and a wrong capacity number gets someone assigned work they cannot do. These pin the
/// rules the schedule claims to follow.
/// </summary>
public sealed class StaffSchedulingTests : IDisposable
{
    private readonly TestHarness _harness = new();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    public void Dispose() => _harness.Dispose();

    private DateOnly MondayThisWeek =>
        _today.AddDays(-(((int)_today.DayOfWeek + 6) % 7));

    private Person SeedPerson(string name = "Shehan Cooray")
    {
        var person = new Person(name, Role.Developer, null, null, null);
        _harness.Db.People.Add(person);
        _harness.Db.SaveChanges();
        return person;
    }

    private Board SeedBoard(string title = "Discharge Revamp")
    {
        var board = new Board(title, "Discharge", "Aurora", "Q3", BoardStatus.OnTrack, 40, "tester");
        _harness.Db.Boards.Add(board);
        _harness.Db.SaveChanges();
        return board;
    }

    private SquadMember Assign(Board board, Person person, int? allocation,
        DateOnly? startsOn = null, DateOnly? endsOn = null)
    {
        var member = board.AddMember(person, Role.Developer, null, allocation);
        member.Schedule(startsOn, endsOn);
        _harness.Db.SquadMembers.Add(member);
        _harness.Db.SaveChanges();
        return member;
    }

    private Task<StaffScheduleDto> ScheduleAsync(int weeks = 4) =>
        new GetStaffScheduleQueryHandler(_harness.Db)
            .Handle(new GetStaffScheduleQuery(MondayThisWeek, weeks), default);

    // ------------------------------------------------------------------
    // Capacity
    // ------------------------------------------------------------------

    [Fact]
    public async Task Free_capacity_is_availability_minus_commitment()
    {
        var person = SeedPerson();
        Assign(SeedBoard(), person, 60);

        var week = (await ScheduleAsync()).People.Single().Weeks[0];

        week.AvailablePercent.Should().Be(100);
        week.CommittedPercent.Should().Be(60);
        week.FreePercent.Should().Be(40);
        week.OverCommitted.Should().BeFalse();
    }

    [Fact]
    public async Task Allocations_across_boards_add_up_and_can_overcommit()
    {
        var person = SeedPerson();
        Assign(SeedBoard("Discharge"), person, 60);
        Assign(SeedBoard("Admission"), person, 60);

        var week = (await ScheduleAsync()).People.Single().Weeks[0];

        // 120% is the number worth seeing; clamping it to 100 would hide the problem.
        week.CommittedPercent.Should().Be(120);
        week.FreePercent.Should().Be(-20);
        week.OverCommitted.Should().BeTrue();
    }

    [Fact]
    public async Task Leave_reduces_the_capacity_available_that_week()
    {
        var person = SeedPerson();
        Assign(SeedBoard(), person, 60);

        _harness.Db.PersonAvailability.Add(new PersonAvailability(
            person.Id, MondayThisWeek, MondayThisWeek.AddDays(4),
            AvailabilityKind.Leave, 0, "Annual leave", "admin"));
        await _harness.Db.SaveChangesAsync();

        var week = (await ScheduleAsync()).People.Single().Weeks[0];

        week.AvailablePercent.Should().Be(0);
        week.FreePercent.Should().Be(-60);
        week.OverCommitted.Should().BeTrue();
        week.AwayReasons.Should().Contain("Leave");
    }

    [Fact]
    public async Task The_most_restrictive_availability_record_wins()
    {
        var person = SeedPerson();

        // Part-time all month, and on leave for part of this week. The leave is the real
        // constraint; taking the larger of the two would overstate what they can take on.
        _harness.Db.PersonAvailability.Add(new PersonAvailability(
            person.Id, MondayThisWeek, MondayThisWeek.AddDays(27),
            AvailabilityKind.PartTime, 60, null, "admin"));
        _harness.Db.PersonAvailability.Add(new PersonAvailability(
            person.Id, MondayThisWeek, MondayThisWeek.AddDays(2),
            AvailabilityKind.Leave, 0, null, "admin"));
        await _harness.Db.SaveChangesAsync();

        var week = (await ScheduleAsync()).People.Single().Weeks[0];

        week.AvailablePercent.Should().Be(0);
        week.AwayReasons.Should().Contain("Leave").And.Contain("Part time");
    }

    // ------------------------------------------------------------------
    // Dates
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_assignment_only_counts_in_the_weeks_it_runs()
    {
        var person = SeedPerson();
        var thirdWeek = MondayThisWeek.AddDays(14);

        Assign(SeedBoard(), person, 80, thirdWeek, thirdWeek.AddDays(6));

        var weeks = (await ScheduleAsync()).People.Single().Weeks;

        weeks[0].CommittedPercent.Should().Be(0);
        weeks[1].CommittedPercent.Should().Be(0);
        weeks[2].CommittedPercent.Should().Be(80);
        weeks[3].CommittedPercent.Should().Be(0);
    }

    [Fact]
    public async Task An_open_ended_assignment_runs_from_its_start_onwards()
    {
        var person = SeedPerson();
        Assign(SeedBoard(), person, 50, MondayThisWeek.AddDays(7), null);

        var weeks = (await ScheduleAsync()).People.Single().Weeks;

        weeks[0].CommittedPercent.Should().Be(0);
        weeks.Skip(1).Should().OnlyContain(w => w.CommittedPercent == 50);
    }

    [Fact]
    public async Task An_assignment_with_no_dates_counts_in_every_week()
    {
        // Most assignments are simply "until further notice", and treating those as
        // scheduled-for-nothing would report a team with no commitments at all.
        var person = SeedPerson();
        Assign(SeedBoard(), person, 40);

        var weeks = (await ScheduleAsync()).People.Single().Weeks;

        weeks.Should().OnlyContain(w => w.CommittedPercent == 40);
    }

    [Fact]
    public void An_assignment_cannot_end_before_it_starts()
    {
        var person = SeedPerson();
        var board = SeedBoard();
        var member = board.AddMember(person, Role.Developer, null, 50);

        var act = () => member.Schedule(MondayThisWeek.AddDays(7), MondayThisWeek);

        act.Should().Throw<DomainException>().WithMessage("*before it starts*");
    }

    // ------------------------------------------------------------------
    // Honesty about the data
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_assignment_with_no_allocation_counts_as_zero_and_is_reported()
    {
        var person = SeedPerson();
        Assign(SeedBoard(), person, null);

        var schedule = await ScheduleAsync();

        // Guessing a share would invent a commitment nobody made; the coverage note is how
        // the reader learns the total is understated.
        schedule.People.Single().Weeks[0].CommittedPercent.Should().Be(0);
        schedule.Coverage.AssignmentsWithoutAllocation.Should().Be(1);
        schedule.Coverage.Note.Should().Contain("understated");
    }

    // ------------------------------------------------------------------
    // Work items
    // ------------------------------------------------------------------

    [Fact]
    public void Completing_a_work_item_stamps_the_date_and_reopening_clears_it()
    {
        var board = SeedBoard();
        var item = new WorkItem(board.Id, "Write the BRD", null, WorkItemStatus.InProgress,
            null, null, null, null, "tester");

        item.CompletedOn.Should().BeNull();

        item.Update("Write the BRD", null, WorkItemStatus.Done, null, null, null, null);
        item.CompletedOn.Should().Be(_today);

        // A re-opened task that keeps a completion date claims something untrue.
        item.Update("Write the BRD", null, WorkItemStatus.InProgress, null, null, null, null);
        item.CompletedOn.Should().BeNull();
    }

    [Fact]
    public void Only_an_unfinished_item_past_its_planned_end_is_overdue()
    {
        var board = SeedBoard();
        var yesterday = _today.AddDays(-1);

        var late = new WorkItem(board.Id, "UAT", null, WorkItemStatus.InProgress,
            null, yesterday, null, null, "tester");
        var doneLate = new WorkItem(board.Id, "UAT", null, WorkItemStatus.Done,
            null, yesterday, null, null, "tester");
        var undated = new WorkItem(board.Id, "UAT", null, WorkItemStatus.InProgress,
            null, null, null, null, "tester");

        late.IsOverdue(_today).Should().BeTrue();
        doneLate.IsOverdue(_today).Should().BeFalse();
        // No planned end is not the same as late, and must never be reported as such.
        undated.IsOverdue(_today).Should().BeFalse();
    }

    [Fact]
    public async Task The_schedule_counts_open_and_overdue_work_per_person()
    {
        var person = SeedPerson();
        var board = SeedBoard();
        Assign(board, person, 50);

        _harness.Db.WorkItems.Add(new WorkItem(board.Id, "Overdue", person.Id,
            WorkItemStatus.InProgress, null, _today.AddDays(-2), null, null, "tester"));
        _harness.Db.WorkItems.Add(new WorkItem(board.Id, "Open", person.Id,
            WorkItemStatus.ToDo, null, _today.AddDays(5), null, null, "tester"));
        _harness.Db.WorkItems.Add(new WorkItem(board.Id, "Finished", person.Id,
            WorkItemStatus.Done, null, null, null, null, "tester"));
        await _harness.Db.SaveChangesAsync();

        var row = (await ScheduleAsync()).People.Single();

        row.OpenWorkItems.Should().Be(2);
        row.OverdueWorkItems.Should().Be(1);
    }

    [Fact]
    public void A_work_item_needs_a_title_and_a_board()
    {
        var board = SeedBoard();

        var noTitle = () => new WorkItem(board.Id, "  ", null, WorkItemStatus.ToDo,
            null, null, null, null, "tester");
        noTitle.Should().Throw<DomainException>().WithMessage("*title*");

        var noBoard = () => new WorkItem(Guid.Empty, "Task", null, WorkItemStatus.ToDo,
            null, null, null, null, "tester");
        noBoard.Should().Throw<DomainException>().WithMessage("*board*");
    }

    // ------------------------------------------------------------------
    // Person profile
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_profile_gathers_commitments_work_and_activity()
    {
        var person = SeedPerson();
        var board = SeedBoard();
        Assign(board, person, 70);

        _harness.Db.WorkItems.Add(new WorkItem(board.Id, "Write the BRD", person.Id,
            WorkItemStatus.Blocked, null, null, null, null, "tester"));
        _harness.Db.BoardAuditEntries.Add(new BoardAuditEntry(
            board.Id, "Progress", "40", "55", person.FullName));
        await _harness.Db.SaveChangesAsync();

        var profile = await new GetPersonProfileQueryHandler(_harness.Db)
            .Handle(new GetPersonProfileQuery(person.Id), default);

        profile.Capacity.CommittedPercent.Should().Be(70);
        profile.Capacity.FreePercent.Should().Be(30);
        profile.Assignments.Should().ContainSingle().Which.BoardTitle.Should().Be("Discharge Revamp");
        profile.Work.Blocked.Should().Be(1);
        profile.Activity.Should().ContainSingle();
    }

    [Fact]
    public async Task The_profile_says_when_it_could_not_match_any_activity()
    {
        // Activity is matched on the recorded display name, so a person who has never
        // edited a board — or was renamed — legitimately shows none. Saying so beats an
        // empty list that looks like a bug.
        var person = SeedPerson();

        var profile = await new GetPersonProfileQueryHandler(_harness.Db)
            .Handle(new GetPersonProfileQuery(person.Id), default);

        profile.Activity.Should().BeEmpty();
        profile.ActivityNote.Should().Contain("display name");
    }

    [Fact]
    public async Task Work_on_a_deleted_board_disappears_from_a_persons_list()
    {
        var person = SeedPerson();
        var board = SeedBoard();

        _harness.Db.WorkItems.Add(new WorkItem(board.Id, "Task", person.Id,
            WorkItemStatus.ToDo, null, null, null, null, "tester"));
        await _harness.Db.SaveChangesAsync();

        board.SoftDelete();
        await _harness.Db.SaveChangesAsync();

        var profile = await new GetPersonProfileQueryHandler(_harness.Db)
            .Handle(new GetPersonProfileQuery(person.Id), default);

        // Without a matching query filter these keep showing up forever.
        profile.WorkItems.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Availability rules
    // ------------------------------------------------------------------

    [Fact]
    public void An_availability_period_cannot_end_before_it_starts()
    {
        var person = SeedPerson();

        var act = () => new PersonAvailability(person.Id, _today.AddDays(3), _today,
            AvailabilityKind.Leave, 0, null, "admin");

        act.Should().Throw<DomainException>().WithMessage("*before it starts*");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Remaining_capacity_must_be_a_percentage(int capacity)
    {
        var person = SeedPerson();

        var act = () => new PersonAvailability(person.Id, _today, _today,
            AvailabilityKind.Leave, capacity, null, "admin");

        act.Should().Throw<DomainException>().WithMessage("*between 0 and 100*");
    }

    [Fact]
    public void A_period_covers_both_of_its_end_days()
    {
        var person = SeedPerson();
        var record = new PersonAvailability(person.Id, _today, _today.AddDays(2),
            AvailabilityKind.Leave, 0, null, "admin");

        record.Covers(_today).Should().BeTrue();
        record.Covers(_today.AddDays(2)).Should().BeTrue();
        record.Covers(_today.AddDays(3)).Should().BeFalse();
    }
}
