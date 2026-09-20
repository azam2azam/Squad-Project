using Application.Abstractions;
using Application.People.Profiles;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// Who may write to a profile, and what the domain refuses outright. The permission rule is
/// the part worth pinning: a directory where anybody can rewrite anybody's page is not a
/// directory, it is a wiki nobody trusts.
/// </summary>
public sealed class PersonProfileTests : IDisposable
{
    private readonly TestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private Person SeedPerson(string name = "Shehan Cooray")
    {
        var person = new Person(name, Role.Developer);
        _harness.Db.People.Add(person);
        _harness.Db.SaveChanges();
        return person;
    }

    private AppUser SeedUser(UserRole role, Guid? personId = null, string name = "Shehan Cooray")
    {
        var user = new AppUser($"{Guid.NewGuid():N}@pirt.example", name, role, "hash");
        if (personId is { } id) user.LinkToPerson(id);

        _harness.Db.Users.Add(user);
        _harness.Db.SaveChanges();

        _harness.AsRole(role, user.Id);
        return user;
    }

    private UpdatePersonProfileCommandHandler UpdateHandler =>
        new(_harness.Db, _harness.UserContext, new NoSender());

    private AddPersonSkillsCommandHandler SkillHandler =>
        new(_harness.Db, _harness.UserContext, new NoSender());

    // -----------------------------------------------------------------------
    // Permissions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_person_may_edit_their_own_profile()
    {
        var person = SeedPerson();
        SeedUser(UserRole.ProductOwner, person.Id);

        await UpdateHandler.Handle(
            new UpdatePersonProfileCommand(person.Id, "Tech Lead", "Java full stack."), default);

        var saved = await _harness.Db.People.FirstAsync();
        saved.Headline.Should().Be("Tech Lead");
        saved.About.Should().Be("Java full stack.");
    }

    [Fact]
    public async Task A_viewer_may_still_edit_their_own_profile()
    {
        // Viewers are read-only everywhere else. Their own page is the exception, and
        // deliberately so: it is the one thing in the application that is theirs.
        var person = SeedPerson();
        SeedUser(UserRole.Viewer, person.Id);

        await UpdateHandler.Handle(
            new UpdatePersonProfileCommand(person.Id, "QA", null), default);

        (await _harness.Db.People.FirstAsync()).Headline.Should().Be("QA");
    }

    [Fact]
    public async Task Somebody_else_may_not_edit_a_profile()
    {
        var person = SeedPerson();
        var otherPerson = SeedPerson("Udith Perera");
        SeedUser(UserRole.ProductOwner, otherPerson.Id, "Udith Perera");

        var act = () => UpdateHandler.Handle(
            new UpdatePersonProfileCommand(person.Id, "Hacked", null), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await _harness.Db.People.FirstAsync(p => p.Id == person.Id)).Headline.Should().BeNull();
    }

    [Fact]
    public async Task An_account_linked_to_nobody_may_not_edit_a_profile()
    {
        var person = SeedPerson();
        SeedUser(UserRole.ProductOwner);

        var act = () => UpdateHandler.Handle(
            new UpdatePersonProfileCommand(person.Id, "Nope", null), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task An_admin_may_edit_anybody()
    {
        var person = SeedPerson();
        SeedUser(UserRole.Admin, personId: null, name: "Administrator");

        await UpdateHandler.Handle(
            new UpdatePersonProfileCommand(person.Id, "Chief Architect", null), default);

        (await _harness.Db.People.FirstAsync()).Headline.Should().Be("Chief Architect");
    }

    // -----------------------------------------------------------------------
    // Skills
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_comma_separated_list_becomes_several_skills()
    {
        var person = SeedPerson();
        SeedUser(UserRole.Admin);

        await SkillHandler.Handle(
            new AddPersonSkillsCommand(person.Id, "Angular, FHIR R4 , SQL Server"), default);

        var skills = await _harness.Db.PersonSkills.OrderBy(s => s.OrderIndex).ToListAsync();
        skills.Select(s => s.Name).Should().Equal("Angular", "FHIR R4", "SQL Server");
    }

    [Fact]
    public async Task The_same_skill_in_different_casing_is_not_added_twice()
    {
        // A directory that lists "Angular" and "angular" separately is a worse directory.
        var person = SeedPerson();
        SeedUser(UserRole.Admin);

        await SkillHandler.Handle(new AddPersonSkillsCommand(person.Id, "Angular"), default);
        await SkillHandler.Handle(new AddPersonSkillsCommand(person.Id, "angular, RxJS"), default);

        var skills = await _harness.Db.PersonSkills.ToListAsync();
        skills.Select(s => s.Name).Should().BeEquivalentTo(["Angular", "RxJS"]);
    }

    [Fact]
    public async Task An_empty_skill_list_is_refused_rather_than_silently_ignored()
    {
        var person = SeedPerson();
        SeedUser(UserRole.Admin);

        var act = () => SkillHandler.Handle(new AddPersonSkillsCommand(person.Id, " , ,"), default);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Removing_a_skill_closes_the_gap_it_leaves()
    {
        var person = SeedPerson();
        SeedUser(UserRole.Admin);

        await SkillHandler.Handle(new AddPersonSkillsCommand(person.Id, "A, B, C"), default);

        var middle = await _harness.Db.PersonSkills.FirstAsync(s => s.Name == "B");

        await new RemovePersonSkillCommandHandler(_harness.Db, _harness.UserContext, new NoSender())
            .Handle(new RemovePersonSkillCommand(person.Id, middle.Id), default);

        var remaining = await _harness.Db.PersonSkills.OrderBy(s => s.OrderIndex).ToListAsync();
        remaining.Select(s => s.Name).Should().Equal("A", "C");
        remaining.Select(s => s.OrderIndex).Should().Equal(0, 1);
    }

    // -----------------------------------------------------------------------
    // Photos
    // -----------------------------------------------------------------------

    [Fact]
    public void A_photo_that_is_not_an_image_is_refused()
    {
        var person = SeedPerson();

        var act = () => person.SetPhoto([1, 2, 3], "application/pdf");

        act.Should().Throw<DomainException>().WithMessage("*JPEG, PNG, WebP or GIF*");
    }

    [Fact]
    public void A_photo_over_the_size_limit_is_refused()
    {
        var person = SeedPerson();

        var act = () => person.SetPhoto(new byte[PersonPhoto.MaxBytes + 1], "image/png");

        act.Should().Throw<DomainException>().WithMessage("*2 MB*");
    }

    [Fact]
    public void Clearing_the_photo_leaves_no_row_behind()
    {
        var person = SeedPerson();
        person.SetPhoto([1, 2, 3], "image/png");
        person.Photo.Should().NotBeNull();

        person.SetPhoto(null, null);

        person.Photo.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Achievements
    // -----------------------------------------------------------------------

    [Fact]
    public void An_achievement_needs_a_title_but_not_a_date()
    {
        var person = SeedPerson();

        var act = () => person.AddAchievement("  ", null, null);
        act.Should().Throw<DomainException>();

        var win = person.AddAchievement("Led the MOH S3 release", null, null);
        win.AchievedOn.Should().BeNull();
        person.Achievements.Should().ContainSingle();
    }
}

/// <summary>
/// The handlers re-read the profile through MediatR after saving, which these tests do not
/// need — they assert on the database. This stands in for the mediator so a handler can be
/// constructed without wiring the whole pipeline.
/// </summary>
internal sealed class NoSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default) => Task.FromResult(default(TResponse)!);

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        Task.FromResult<object?>(null);

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest => Task.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(object request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
