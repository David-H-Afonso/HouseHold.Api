using System.Text.Json;
using Household.Api.Application.Services;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public class HouseholdConsumerConnectionServiceTests
{
    [Fact]
    public async Task GetConnections_ReturnsFiveProvidersAndIsolatesUsers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userA = CreateUser("a@example.test");
        var userB = CreateUser("b@example.test");
        db.Users.AddRange(userA, userB);
        db.HouseholdConsumerConnections.AddRange(
            CreateConnection(userA.Id, "Account A", HouseholdConnectionStatus.Connected),
            CreateConnection(userB.Id, "Account B", HouseholdConnectionStatus.Error)
        );
        await db.SaveChangesAsync();

        var service = new HouseholdConsumerConnectionService(
            db,
            CreateRegistry(),
            new HouseholdConnectionCoordinator(),
            new TestHttpClientFactory(),
            new EphemeralDataProtectionProvider()
        );

        var result = await service.GetConnectionsAsync(userA.Id, CancellationToken.None);

        Assert.Equal(5, result.Count);
        var doIt = result.Single(item => item.Provider == "doit");
        Assert.Equal(HouseholdConnectionStatus.Connected, doIt.Status);
        Assert.Equal("Account A", doIt.AccountDisplayName);
        Assert.DoesNotContain(result, item => item.AccountDisplayName == "Account B");
        Assert.Equal(4, result.Count(item => item.Status == HouseholdConnectionStatus.Disconnected));
    }

    [Fact]
    public void ConnectionDto_DoesNotExposeTokenOrAuthorizationSecrets()
    {
        var forbidden = new[] { "token", "secret", "verifier", "code", "protected" };
        var propertyNames = typeof(HouseholdConnectionDto).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(
            propertyNames,
            name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
        );

        var json = JsonSerializer.Serialize(
            new HouseholdConnectionDto(
                "doit",
                "DoIt",
                true,
                "https://doit.example",
                HouseholdConnectionStatus.Disconnected,
                null,
                null,
                [],
                null,
                null,
                null
            ),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    private static User CreateUser(string email) =>
        new() { Email = email, UserName = email, PasswordHash = "test", IsActive = true };

    private static HouseholdConsumerConnection CreateConnection(
        Guid userId,
        string displayName,
        HouseholdConnectionStatus status
    ) =>
        new()
        {
            UserId = userId,
            Provider = "doit",
            ProtectedAccessToken = "protected-access",
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ProtectedRefreshToken = "protected-refresh",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(10),
            SourceConnectionId = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid().ToString(),
            AccountDisplayName = displayName,
            GrantedScopes = "profile.read tasks.read",
            Status = status,
            ConnectedAt = DateTime.UtcNow,
        };

    private static HouseholdProviderRegistry CreateRegistry() =>
        new(
            Options.Create(
                new HouseholdConnectionSettings
                {
                    PublicUrl = "https://household.example",
                    ApiPublicUrl = "https://api.household.example",
                    DoItBaseUrl = "https://doit-api.example",
                    DoItOpenUrl = "https://doit.example",
                }
            )
        );

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
