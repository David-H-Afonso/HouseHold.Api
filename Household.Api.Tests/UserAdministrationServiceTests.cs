using Household.Api.Application.Services;
using Household.Api.DTOs;
using Household.Api.Models.Auth;

namespace Household.Api.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact]
    public async Task CannotDisableOrDemoteLastActiveAdmin()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        admin.IsAdmin = true;
        await fixture.Db.SaveChangesAsync();
        var service = new UserAdministrationService(fixture.Db);

        var result = await service.UpdateUserAsync(admin.Id, admin.Id,
            new AdminUpdateUserRequest(admin.Email, admin.UserName, false, true), CancellationToken.None);

        Assert.Equal("last_admin", result.Error);
        Assert.True(admin.IsAdmin);
    }

    [Fact]
    public async Task Invitation_IsHashedSingleUseAndCreatesOnlyOneUser()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        admin.IsAdmin = true;
        await fixture.Db.SaveChangesAsync();
        var service = new UserAdministrationService(fixture.Db);

        var created = await service.CreateInvitationAsync(admin.Id,
            new CreateInvitationRequest("new@example.test", "New User"), CancellationToken.None);
        Assert.NotNull(created.Invitation);
        Assert.DoesNotContain(created.Invitation.Token, fixture.Db.UserInvitations.Single().TokenHash);

        var first = await service.RedeemInvitationAsync(
            new RedeemInvitationRequest(created.Invitation.Token, "Strong!Password9"), CancellationToken.None);
        var replay = await service.RedeemInvitationAsync(
            new RedeemInvitationRequest(created.Invitation.Token, "Strong!Password9"), CancellationToken.None);

        Assert.NotNull(first.User);
        Assert.Equal("invalid_invitation", replay.Error);
        var invitedUser = Assert.Single(fixture.Db.Users.Where(user => user.Email == "new@example.test"));
        Assert.False(invitedUser.RequiresPasswordChange);
    }

    [Fact]
    public async Task PasswordReset_RevokesRefreshSessionsAndAdvancesSessionVersion()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        admin.IsAdmin = true;
        var target = await fixture.AddUserAsync("user@example.test");
        fixture.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = target.Id,
            TokenHash = "hash-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        await fixture.Db.SaveChangesAsync();
        var service = new UserAdministrationService(fixture.Db);

        var result = await service.ResetPasswordAsync(admin.Id, target.Id, CancellationToken.None);

        Assert.NotNull(result.TemporaryPassword);
        Assert.True(target.RequiresPasswordChange);
        Assert.Equal(1, target.SessionVersion);
        Assert.NotNull(fixture.Db.RefreshTokens.Single().RevokedAt);
    }

    [Fact]
    public async Task DirectCreation_RequiresChangingTheTemporaryPassword()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        var service = new UserAdministrationService(fixture.Db);

        var result = await service.CreateUserAsync(
            admin.Id,
            new AdminCreateUserRequest("new@example.test", "New User", null),
            CancellationToken.None
        );

        Assert.NotNull(result.TemporaryPassword);
        Assert.Null(result.Error);
        Assert.True(fixture.Db.Users.Single(user => user.Email == "new@example.test").RequiresPasswordChange);
    }

    [Fact]
    public async Task ControlCharactersInDisplayName_AreRejected()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        var service = new UserAdministrationService(fixture.Db);

        var result = await service.CreateUserAsync(
            admin.Id,
            new AdminCreateUserRequest("new@example.test", "New\r\nUser", "Strong!Password9"),
            CancellationToken.None
        );

        Assert.Equal("invalid_name", result.Error);
    }

    [Fact]
    public async Task Update_AuditsIdentityRoleAndActivationAsDistinctActions()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var admin = await fixture.AddUserAsync("admin@example.test");
        admin.IsAdmin = true;
        var target = await fixture.AddUserAsync("user@example.test");
        await fixture.Db.SaveChangesAsync();
        var service = new UserAdministrationService(fixture.Db);

        var result = await service.UpdateUserAsync(
            admin.Id,
            target.Id,
            new AdminUpdateUserRequest("renamed@example.test", "Renamed", true, false),
            CancellationToken.None
        );

        Assert.Null(result.Error);
        var actions = fixture.Db.AuditEvents.Where(item => item.TargetUserId == target.Id).Select(item => item.Action).ToList();
        Assert.Contains("user.identity_changed", actions);
        Assert.Contains("user.role_admin_granted", actions);
        Assert.Contains("user.deactivated", actions);
        Assert.DoesNotContain("user.updated", actions);
        Assert.All(fixture.Db.AuditEvents, item => Assert.Null(item.SummaryJson));
    }
}
