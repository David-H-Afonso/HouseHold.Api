using Household.Api.Application.Services;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Tests;

public sealed class UserSettingsServiceTests
{
    [Fact]
    public async Task PreferencesAndLayout_AreIsolatedByUser()
    {
        await using var fixture = await TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        var service = new UserSettingsService(fixture.Db);

        await service.UpdatePreferencesAsync(userA.Id, new UpdateUserPreferencesRequest(
            1, "Asia/Tokyo", "dark", "showdown", [3, 1], ["David-H-Afonso/DoIt.Api"], "abc-123"), CancellationToken.None);
        var a = await service.GetPreferencesAsync(userA.Id, CancellationToken.None);
        var b = await service.GetPreferencesAsync(userB.Id, CancellationToken.None);

        Assert.Equal("Asia/Tokyo", a.TimeZoneId);
        Assert.Equal("showdown", a.PokemonSpriteSource);
        Assert.Null(b.TimeZoneId);
        Assert.Equal("home", b.PokemonSpriteSource);

        var layoutA = await service.GetLayoutAsync(userA.Id, CancellationToken.None);
        var modified = layoutA.Widgets.Select(item => item.Type == "pokemon" ? item with { Visible = true } : item).ToList();
        await service.UpdateLayoutAsync(userA.Id, new UpdateDashboardLayoutRequest(1, modified), CancellationToken.None);
        var layoutB = await service.GetLayoutAsync(userB.Id, CancellationToken.None);
        Assert.True((await service.GetLayoutAsync(userA.Id, CancellationToken.None)).Widgets.Single(item => item.Type == "pokemon").Visible);
        Assert.False(layoutB.Widgets.Single(item => item.Type == "pokemon").Visible);
    }

    [Fact]
    public async Task FirstRead_LeavesTimezoneUnsetUntilBrowserPatchesIt()
    {
        await using var fixture = await TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("first-use@example.test");
        var service = new UserSettingsService(fixture.Db);

        var first = await service.GetPreferencesAsync(user.Id, CancellationToken.None);
        var patched = await service.UpdatePreferencesAsync(
            user.Id,
            new UpdateUserPreferencesRequest(1, "Europe/Madrid", null, null, null, null, null),
            CancellationToken.None
        );

        Assert.Null(first.TimeZoneId);
        Assert.Equal("Europe/Madrid", patched.TimeZoneId);
    }

    [Fact]
    public async Task InvalidTimezoneAndUnknownRepository_AreRejected()
    {
        await using var fixture = await TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("a@example.test");
        var service = new UserSettingsService(fixture.Db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePreferencesAsync(user.Id,
            new UpdateUserPreferencesRequest(1, "Mars/Olympus", null, null, null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePreferencesAsync(user.Id,
            new UpdateUserPreferencesRequest(1, null, null, null, null, ["other/repository"], null), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdatePreferencesAsync(user.Id,
            new UpdateUserPreferencesRequest(1, null, null, "HOME", null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task JellyfinPreferenceChange_PreservesActiveSeerrOverrideReservation()
    {
        await using var fixture = await TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("override@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            JellyfinUserId = "old-jellyfin-id",
            SeerrUserIdOverride = 7,
            SeerrResolvedUserId = 7,
        });
        await fixture.Db.SaveChangesAsync();
        var service = new UserSettingsService(fixture.Db);

        await service.UpdatePreferencesAsync(
            user.Id,
            new UpdateUserPreferencesRequest(1, null, null, null, null, null, "new-jellyfin-id"),
            CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var preference = fixture.Db.UserPreferences.Single();
        Assert.Equal(7, preference.SeerrUserIdOverride);
        Assert.Equal(7, preference.SeerrResolvedUserId);
        Assert.False(preference.SeerrJellyfinMappingApproved);
    }

    [Fact]
    public async Task DashboardPositions_MustBeZeroBasedAndContiguous()
    {
        await using var fixture = await TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("a@example.test");
        var service = new UserSettingsService(fixture.Db);
        var layout = await service.GetLayoutAsync(user.Id, CancellationToken.None);
        var invalid = layout.Widgets.Select(item => item with { Position = item.Position + 10 }).ToList();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateLayoutAsync(
            user.Id,
            new UpdateDashboardLayoutRequest(1, invalid),
            CancellationToken.None
        ));
    }

    internal sealed class TestDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDbContext Db { get; }

        private TestDb(SqliteConnection connection, AppDbContext db) { _connection = connection; Db = db; }

        public static async Task<TestDb> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new TestDb(connection, db);
        }

        public AppDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        public async Task<User> AddUserAsync(string email)
        {
            var user = new User { Email = email, UserName = email, PasswordHash = "hash" };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
