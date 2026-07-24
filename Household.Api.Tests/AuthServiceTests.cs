using Household.Api.Configuration;
using Household.Api.Models.Auth;
using Household.Api.Services;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class AuthServiceTests
{
    private const string CurrentPassword = "Current!Password9";

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_DoesNotChangeCredentialsOrSessions()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await AddUserAsync(fixture, CurrentPassword);
        var session = await AddSessionAsync(fixture, user.Id, "wrong-current-session");
        var originalHash = user.PasswordHash;
        var service = CreateService(fixture);

        var error = await service.ChangePasswordAsync(
            user.Id,
            "Wrong!Password9",
            "Replacement!Password8",
            CancellationToken.None
        );

        Assert.Equal("invalid_current_password", error);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Equal(0, user.SessionVersion);
        Assert.Null(session.RevokedAt);
        Assert.Empty(fixture.Db.AuditEvents);
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_DoesNotChangeCredentialsOrSessions()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await AddUserAsync(fixture, CurrentPassword);
        var session = await AddSessionAsync(fixture, user.Id, "weak-password-session");
        var originalHash = user.PasswordHash;
        var service = CreateService(fixture);

        var error = await service.ChangePasswordAsync(
            user.Id,
            CurrentPassword,
            "too-weak",
            CancellationToken.None
        );

        Assert.Equal("password_too_weak", error);
        Assert.Equal(originalHash, user.PasswordHash);
        Assert.Equal(0, user.SessionVersion);
        Assert.Null(session.RevokedAt);
        Assert.Empty(fixture.Db.AuditEvents);
    }

    [Fact]
    public async Task ChangePassword_Success_ChangesHashRevokesSessionsAndAdvancesVersion()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await AddUserAsync(fixture, CurrentPassword);
        user.RequiresPasswordChange = true;
        user.SessionVersion = 4;
        var activeSession = await AddSessionAsync(fixture, user.Id, "active-session");
        var expiredSession = await AddSessionAsync(fixture, user.Id, "expired-session", DateTime.UtcNow.AddMinutes(-1));
        var originalHash = user.PasswordHash;
        var service = CreateService(fixture);
        const string newPassword = "Replacement!Password8";

        var error = await service.ChangePasswordAsync(
            user.Id,
            CurrentPassword,
            newPassword,
            CancellationToken.None
        );

        Assert.Null(error);
        Assert.NotEqual(originalHash, user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash));
        Assert.False(user.RequiresPasswordChange);
        Assert.Equal(5, user.SessionVersion);
        Assert.NotNull(activeSession.RevokedAt);
        Assert.NotNull(expiredSession.RevokedAt);

        var audit = Assert.Single(fixture.Db.AuditEvents);
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Equal(user.Id, audit.TargetUserId);
        Assert.Equal("user.password_changed", audit.Action);
        Assert.Null(audit.SummaryJson);
    }

    [Fact]
    public async Task Login_ReportsAndClaimsRequiredPasswordChange()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await AddUserAsync(fixture, CurrentPassword);
        user.RequiresPasswordChange = true;
        await fixture.Db.SaveChangesAsync();
        var service = CreateService(fixture);

        var result = await service.LoginAsync(user.Email, CurrentPassword, null, null);

        Assert.NotNull(result);
        Assert.True(result.RequiresPasswordChange);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal("true", token.Claims.Single(claim => claim.Type == "requiresPasswordChange").Value);
    }

    [Fact]
    public async Task Refresh_RequiredPasswordChange_DoesNotIssueOrRotateTokens()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await AddUserAsync(fixture, CurrentPassword);
        user.RequiresPasswordChange = true;
        const string rawRefreshToken = "required-change-refresh-token";
        var session = await AddSessionAsync(fixture, user.Id, AuthService.HashToken(rawRefreshToken));
        await fixture.Db.SaveChangesAsync();
        var service = CreateService(fixture);

        var result = await service.RefreshAsync(rawRefreshToken, null, null);

        Assert.Null(result);
        Assert.Null(session.RevokedAt);
        Assert.Single(fixture.Db.RefreshTokens);
    }

    private static AuthService CreateService(UserSettingsServiceTests.TestDb fixture) =>
        new(
            fixture.Db,
            Options.Create(
                new JwtSettings
                {
                    SecretKey = "test-secret-key-that-is-at-least-32-characters",
                }
            ),
            NullLogger<AuthService>.Instance
        );

    private static async Task<User> AddUserAsync(UserSettingsServiceTests.TestDb fixture, string password)
    {
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            UserName = "Auth Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4),
        };
        fixture.Db.Users.Add(user);
        await fixture.Db.SaveChangesAsync();
        return user;
    }

    private static async Task<RefreshToken> AddSessionAsync(
        UserSettingsServiceTests.TestDb fixture,
        Guid userId,
        string tokenHash,
        DateTime? expiresAt = null
    )
    {
        var session = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
        };
        fixture.Db.RefreshTokens.Add(session);
        await fixture.Db.SaveChangesAsync();
        return session;
    }
}
