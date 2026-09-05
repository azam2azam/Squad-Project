using Application.Abstractions;
using Application.Users;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace Application.Tests;

/// <summary>
/// User administration creates the accounts that everything else authorises against, so
/// the rules that stop an admin locking the organisation out — and the rules that keep
/// passwords out of responses — are pinned here rather than left to review.
/// </summary>
public sealed class UserManagementTests : IDisposable
{
    private readonly TestHarness _harness = new();
    private readonly IdentityPasswordHasher _hasher = new();

    public void Dispose() => _harness.Dispose();

    private AppUser SeedUser(string email, UserRole role, string password = "Seeded!Pass123",
        bool active = true)
    {
        var user = new AppUser(email, email.Split('@')[0], role, _hasher.Hash(password));
        if (!active) user.Deactivate();

        _harness.Db.Users.Add(user);
        _harness.Db.SaveChanges();
        return user;
    }

    /// <summary>Signs the harness in as this account, so "self" rules can be exercised.</summary>
    private void ActAs(AppUser user)
    {
        _harness.UserContext.UserId = user.Id;
        _harness.UserContext.Role = user.Role;
    }

    // ------------------------------------------------------------------
    // Creating
    // ------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_user_stores_a_hash_and_never_returns_the_password()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        ActAs(admin);

        var handler = new CreateUserCommandHandler(_harness.Db, _harness.Authorizer, _hasher);

        var created = await handler.Handle(
            new CreateUserCommand("po@pirt.example", "Pradeep", UserRole.ProductOwner,
                "Squad!Pass2026", null),
            default);

        var stored = await _harness.Db.Users.SingleAsync(u => u.Email == "po@pirt.example");

        stored.PasswordHash.Should().NotBeNullOrWhiteSpace();
        stored.PasswordHash.Should().NotContain("Squad!Pass2026");
        _hasher.Verify("Squad!Pass2026", stored.PasswordHash!).Should().BeTrue();

        // The DTO says whether a password exists; it never carries one.
        created.HasPassword.Should().BeTrue();
        created.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Password") && name != "HasPassword");
    }

    [Fact]
    public async Task Email_is_normalised_and_must_be_unique()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        ActAs(admin);

        var handler = new CreateUserCommandHandler(_harness.Db, _harness.Authorizer, _hasher);

        await handler.Handle(
            new CreateUserCommand("Pradeep@PIRT.example", "Pradeep", UserRole.ProductOwner,
                "Squad!Pass2026", null),
            default);

        // Case must not be a way to create a second account for the same person.
        var act = async () => await handler.Handle(
            new CreateUserCommand("pradeep@pirt.example", "Impostor", UserRole.Admin,
                "Squad!Pass2026", null),
            default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*already has an account*");
    }

    [Fact]
    public async Task A_non_admin_cannot_create_users()
    {
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner);
        ActAs(po);

        var handler = new CreateUserCommandHandler(_harness.Db, _harness.Authorizer, _hasher);

        var act = async () => await handler.Handle(
            new CreateUserCommand("new@pirt.example", "New", UserRole.Admin, "Squad!Pass2026", null),
            default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ------------------------------------------------------------------
    // Not locking yourself out
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_account()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        SeedUser("second@pirt.example", UserRole.Admin);
        ActAs(admin);

        var handler = new SetUserActiveCommandHandler(
            _harness.Db, _harness.Authorizer, _harness.UserContext);

        var act = async () => await handler.Handle(
            new SetUserActiveCommand(admin.Id, false), default);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*cannot deactivate your own account*");
    }

    [Fact]
    public async Task An_admin_cannot_demote_themselves()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        SeedUser("second@pirt.example", UserRole.Admin);
        ActAs(admin);

        var handler = new UpdateUserCommandHandler(
            _harness.Db, _harness.Authorizer, _harness.UserContext);

        var act = async () => await handler.Handle(
            new UpdateUserCommand(admin.Id, "Administrator", UserRole.Viewer, null), default);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*cannot change your own role*");
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        // Reachable when the caller's own token still says Admin but their row no longer
        // does — a stale session racing a demotion. The guard is the net under that.
        var lastAdmin = SeedUser("admin@pirt.example", UserRole.Admin);
        var ghost = SeedUser("ghost@pirt.example", UserRole.Admin, active: false);

        ActAs(ghost);

        var handler = new SetUserActiveCommandHandler(
            _harness.Db, _harness.Authorizer, _harness.UserContext);

        var act = async () => await handler.Handle(
            new SetUserActiveCommand(lastAdmin.Id, false), default);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*last active administrator*");
    }

    // ------------------------------------------------------------------
    // Sessions end when access changes
    // ------------------------------------------------------------------

    [Fact]
    public async Task Deactivating_a_user_ends_their_session()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        var victim = SeedUser("po@pirt.example", UserRole.ProductOwner);
        victim.SetRefreshToken("live-session-hash", DateTimeOffset.UtcNow.AddDays(7));
        await _harness.Db.SaveChangesAsync();

        ActAs(admin);

        await new SetUserActiveCommandHandler(_harness.Db, _harness.Authorizer, _harness.UserContext)
            .Handle(new SetUserActiveCommand(victim.Id, false), default);

        var stored = await _harness.Db.Users.SingleAsync(u => u.Id == victim.Id);

        stored.IsActive.Should().BeFalse();
        // Otherwise a revoked account keeps working until the refresh token expires.
        stored.RefreshTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task Changing_someones_role_ends_their_session()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner);
        po.SetRefreshToken("live-session-hash", DateTimeOffset.UtcNow.AddDays(7));
        await _harness.Db.SaveChangesAsync();

        ActAs(admin);

        await new UpdateUserCommandHandler(_harness.Db, _harness.Authorizer, _harness.UserContext)
            .Handle(new UpdateUserCommand(po.Id, "Pradeep", UserRole.Viewer, null), default);

        var stored = await _harness.Db.Users.SingleAsync(u => u.Id == po.Id);

        stored.Role.Should().Be(UserRole.Viewer);
        // The access token still claims Product Owner until it expires; ending the
        // session is what actually takes the rights away.
        stored.RefreshTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task Resetting_a_password_ends_the_session_it_belonged_to()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner);
        po.SetRefreshToken("live-session-hash", DateTimeOffset.UtcNow.AddDays(7));
        await _harness.Db.SaveChangesAsync();

        ActAs(admin);

        await new ResetUserPasswordCommandHandler(_harness.Db, _harness.Authorizer, _hasher)
            .Handle(new ResetUserPasswordCommand(po.Id, "BrandNew!Pass26"), default);

        var stored = await _harness.Db.Users.SingleAsync(u => u.Id == po.Id);

        _hasher.Verify("BrandNew!Pass26", stored.PasswordHash!).Should().BeTrue();
        stored.RefreshTokenHash.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Changing your own password
    // ------------------------------------------------------------------

    [Fact]
    public async Task Changing_your_own_password_requires_the_current_one()
    {
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner, "Original!Pass26");
        ActAs(po);

        var handler = new ChangeOwnPasswordCommandHandler(
            _harness.Db, _harness.UserContext, _hasher);

        var act = async () => await handler.Handle(
            new ChangeOwnPasswordCommand("WrongGuess!26", "Replacement!26"), default);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*current password is not correct*");

        // And the stored password is untouched.
        var stored = await _harness.Db.Users.SingleAsync(u => u.Id == po.Id);
        _hasher.Verify("Original!Pass26", stored.PasswordHash!).Should().BeTrue();
    }

    [Fact]
    public async Task A_user_can_replace_the_password_an_admin_set()
    {
        // This is what makes an admin-set password acceptable: the admin knows it only
        // until the person replaces it.
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner, "AdminChose!26");
        ActAs(po);

        await new ChangeOwnPasswordCommandHandler(_harness.Db, _harness.UserContext, _hasher)
            .Handle(new ChangeOwnPasswordCommand("AdminChose!26", "TheirOwn!Pass26"), default);

        var stored = await _harness.Db.Users.SingleAsync(u => u.Id == po.Id);

        _hasher.Verify("TheirOwn!Pass26", stored.PasswordHash!).Should().BeTrue();
        _hasher.Verify("AdminChose!26", stored.PasswordHash!).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Listing
    // ------------------------------------------------------------------

    [Fact]
    public async Task Deactivated_accounts_are_hidden_unless_asked_for()
    {
        var admin = SeedUser("admin@pirt.example", UserRole.Admin);
        SeedUser("gone@pirt.example", UserRole.Viewer, active: false);
        ActAs(admin);

        var handler = new ListUsersQueryHandler(_harness.Db, _harness.Authorizer);

        var visible = await handler.Handle(new ListUsersQuery(null, false), default);
        visible.Items.Should().ContainSingle().Which.Email.Should().Be("admin@pirt.example");

        var all = await handler.Handle(new ListUsersQuery(null, true), default);
        all.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_non_admin_cannot_list_users()
    {
        var po = SeedUser("po@pirt.example", UserRole.ProductOwner);
        ActAs(po);

        var act = async () => await new ListUsersQueryHandler(_harness.Db, _harness.Authorizer)
            .Handle(new ListUsersQuery(null, false), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
