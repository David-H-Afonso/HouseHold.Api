using System.Net;
using System.Text;
using Household.Api.Application.Exceptions;
using Household.Api.Configuration;
using Household.Api.Infrastructure.Integrations.Seerr;
using Household.Api.Models.Auth;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class SeerrServiceTests
{
    [Fact]
    public async Task Search_UnapprovedJellyfinMappingNeverCallsSeerrAsApiKeyOwner()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            JellyfinUserId = "jellyfin-user",
            SeerrJellyfinMappingApproved = false,
        });
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() =>
            service.SearchAsync(user.Id, "Dune", 1, CancellationToken.None));

        Assert.Equal("seerr_user_not_mapped", exception.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Search_ApprovedJellyfinMappingAddsUserHeaderAfterProviderResolution()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            JellyfinUserId = "jellyfin-user",
            SeerrJellyfinMappingApproved = true,
        });
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.Contains("/user/jellyfin/")
            ? JsonResponse("{\"id\":42}")
            : JsonResponse("{\"page\":1,\"totalPages\":1,\"totalResults\":1,\"results\":[{\"id\":11,\"mediaType\":\"movie\",\"title\":\"Dune\",\"posterPath\":\"/dune.jpg\"}]}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var result = await service.SearchAsync(user.Id, "Dune", 1, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Null(handler.Requests[0].SeerrUserId);
        Assert.Equal("42", handler.Requests[1].SeerrUserId);
        Assert.Contains("query=Dune", handler.Requests[1].PathAndQuery);
        Assert.Equal(
            "https://seerr.example.test/imageproxy/tmdb/t/p/w600_and_h900_bestv2/dune.jpg",
            Assert.Single(result.Results).PosterPath);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(42, fixture.Db.UserPreferences.Single().SeerrResolvedUserId);
    }

    [Fact]
    public async Task Search_AdminOverrideAlwaysAddsMappedUserHeader()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, SeerrUserIdOverride = 7 });
        var handler = new RecordingHandler(_ =>
            JsonResponse("{\"page\":1,\"totalPages\":1,\"totalResults\":0,\"results\":[]}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        await service.SearchAsync(user.Id, "Dune", 1, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("7", request.SeerrUserId);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(7, fixture.Db.UserPreferences.Single().SeerrResolvedUserId);
    }

    [Fact]
    public async Task Search_DropsUntrustedAbsoluteImageHosts()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, SeerrUserIdOverride = 7 });
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"page\":1,\"totalPages\":1,\"totalResults\":1,\"results\":[{\"id\":11,\"mediaType\":\"movie\",\"title\":\"Dune\",\"posterPath\":\"http://127.0.0.1/private.jpg\"}]}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var result = await service.SearchAsync(user.Id, "Dune", 1, CancellationToken.None);

        Assert.Null(Assert.Single(result.Results).PosterPath);
    }

    [Fact]
    public async Task Mapping_RejectsAnIdentityAlreadyAssignedToAnotherHouseholdUser()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var actor = await fixture.AddUserAsync("admin@example.test");
        var first = await fixture.AddUserAsync("first@example.test");
        var second = await fixture.AddUserAsync("second@example.test");
        var handler = new RecordingHandler(_ => JsonResponse("{\"id\":19}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);
        await service.UpdateUserMappingAsync(
            actor.Id,
            first.Id,
            new("override", null, 19),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => service.UpdateUserMappingAsync(
            actor.Id,
            second.Id,
            new("override", null, 19),
            CancellationToken.None));

        Assert.Equal("seerr_mapping_conflict", exception.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Mapping_RejectsAResolvedIdentityAlreadyAssignedThroughAnotherSource()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var actor = await fixture.AddUserAsync("admin@example.test");
        var first = await fixture.AddUserAsync("first@example.test");
        var second = await fixture.AddUserAsync("second@example.test");
        var handler = new RecordingHandler(_ => JsonResponse("{\"id\":19}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);
        await service.UpdateUserMappingAsync(
            actor.Id,
            first.Id,
            new("jellyfin", "jellyfin-first", null),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => service.UpdateUserMappingAsync(
            actor.Id,
            second.Id,
            new("override", null, 19),
            CancellationToken.None));

        Assert.Equal("seerr_mapping_conflict", exception.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TvRequest_RejectsUnboundedSeasonSelectionBeforeCallingSeerr()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, SeerrUserIdOverride = 7 });
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRequestAsync(
            user.Id,
            new("tv", 99, false, Enumerable.Range(0, 101).ToList()),
            CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Moderation_RequiresMappedManagerBeforeSendingAction()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, SeerrUserIdOverride = 7 });
        var handler = new RecordingHandler(_ => JsonResponse("{\"id\":7,\"permissions\":0}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() =>
            service.ModerateRequestAsync(user.Id, 99, "approve", CancellationToken.None));

        Assert.Equal("seerr_forbidden", exception.Code);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/user/7", request.PathAndQuery);
        Assert.Equal("7", request.SeerrUserId);
    }

    [Fact]
    public async Task RequestViewPermission_CanReadAllWithoutGrantingModeration()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("viewer@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, SeerrUserIdOverride = 7 });
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/api/v1/user/7"
            ? JsonResponse("{\"id\":7,\"permissions\":16384}")
            : JsonResponse("{\"pageInfo\":{\"pages\":1,\"results\":0},\"results\":[]}"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var result = await service.GetRequestsAsync(user.Id, "all", false, 1, CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("7", request.SeerrUserId));
    }

    [Fact]
    public async Task Config_ServerChangeRequiresApiKeyAgain()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = await CreateConfiguredServiceAsync(fixture, handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateConfigAsync(
            new("http://replacement-seerr:5055", "https://seerr.example.test", null),
            CancellationToken.None));

        Assert.Contains("API key", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Config_StaleContextCannotMoveReplacementCredentialsBackToAnOldUrl()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("Household.Seerr.ApiKey.v1");
        var integration = new Integration
        {
            Type = IntegrationType.Seerr,
            Name = "Seerr",
            BaseUrl = "http://original-seerr:5055",
            OpenUrl = "https://seerr.example.test",
            Enabled = true,
        };
        integration.Secrets.Add(new IntegrationSecret
        {
            SecretKey = "api-key",
            ProtectedValue = protector.Protect("original-api-key"),
        });
        fixture.Db.Integrations.Add(integration);
        await fixture.Db.SaveChangesAsync();

        await using var staleDb = fixture.CreateContext();
        await using var replacementDb = fixture.CreateContext();
        _ = await staleDb.Integrations.Include(item => item.Secrets).SingleAsync();
        var handler = new RecordingHandler(_ => JsonResponse("{\"version\":\"1.0.0\"}"));
        var replacementService = CreateService(replacementDb, handler, protection, new SeerrSettings());
        var staleService = CreateService(staleDb, handler, protection, new SeerrSettings());

        await replacementService.UpdateConfigAsync(
            new("http://replacement-seerr:5055", "https://seerr.example.test", "replacement-api-key"),
            CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => staleService.UpdateConfigAsync(
            new("http://original-seerr:5055/path", "https://seerr.example.test", null),
            CancellationToken.None));

        await using var verificationDb = fixture.CreateContext();
        var persisted = await verificationDb.Integrations.Include(item => item.Secrets).SingleAsync();
        Assert.Equal("http://replacement-seerr:5055", persisted.BaseUrl);
        Assert.Equal(
            "replacement-api-key",
            protector.Unprotect(persisted.Secrets.Single(item => item.SecretKey == "api-key").ProtectedValue));
    }

    [Fact]
    public async Task ConfigVersion_RollsBackAStaleSecretWriteWhenAnotherContextChangesTheServer()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("Household.Seerr.ApiKey.v1");
        var integration = new Integration
        {
            Type = IntegrationType.Seerr,
            Name = "Seerr",
            BaseUrl = "http://original-seerr:5055",
            Enabled = true,
        };
        integration.Secrets.Add(new IntegrationSecret
        {
            SecretKey = "api-key",
            ProtectedValue = protector.Protect("original-api-key"),
        });
        fixture.Db.Integrations.Add(integration);
        await fixture.Db.SaveChangesAsync();

        await using var firstDb = fixture.CreateContext();
        await using var staleDb = fixture.CreateContext();
        var first = await firstDb.Integrations.Include(item => item.Secrets).SingleAsync();
        var stale = await staleDb.Integrations.Include(item => item.Secrets).SingleAsync();
        first.BaseUrl = "http://replacement-seerr:5055";
        first.ConfigurationVersion = Guid.NewGuid();
        first.Secrets.Single().ProtectedValue = protector.Protect("replacement-api-key");
        await firstDb.SaveChangesAsync();

        stale.ConfigurationVersion = Guid.NewGuid();
        stale.Secrets.Single().ProtectedValue = protector.Protect("stale-api-key");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());

        await using var verificationDb = fixture.CreateContext();
        var persisted = await verificationDb.Integrations.Include(item => item.Secrets).SingleAsync();
        Assert.Equal("http://replacement-seerr:5055", persisted.BaseUrl);
        Assert.Equal(
            "replacement-api-key",
            protector.Unprotect(persisted.Secrets.Single().ProtectedValue));
    }

    [Fact]
    public async Task ServerChange_InvalidatesJellyfinMappingCacheAcrossServiceInstances()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            JellyfinUserId = "jellyfin-user",
            SeerrJellyfinMappingApproved = true,
        });
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("Household.Seerr.ApiKey.v1");
        var integration = new Integration
        {
            Type = IntegrationType.Seerr,
            Name = "Seerr",
            BaseUrl = "http://old-seerr:5055",
            OpenUrl = "https://seerr.example.test",
            Enabled = true,
        };
        integration.Secrets.Add(new IntegrationSecret
        {
            SecretKey = "api-key",
            ProtectedValue = protector.Protect("test-api-key"),
        });
        fixture.Db.Integrations.Add(integration);
        await fixture.Db.SaveChangesAsync();
        var oldHandler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.Contains("/user/jellyfin/")
            ? JsonResponse("{\"id\":7}")
            : JsonResponse("{\"page\":1,\"totalPages\":1,\"totalResults\":0,\"results\":[]}"));
        var oldService = CreateService(fixture.Db, oldHandler, protection, new SeerrSettings());
        await oldService.SearchAsync(user.Id, "Dune", 1, CancellationToken.None);

        await using (var updateDb = fixture.CreateContext())
        {
            var updated = await updateDb.Integrations.SingleAsync();
            updated.BaseUrl = "http://new-seerr:5055";
            updated.ConfigurationVersion = Guid.NewGuid();
            await updateDb.SaveChangesAsync();
        }

        await using var newDb = fixture.CreateContext();
        var newHandler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.Contains("/user/jellyfin/")
            ? JsonResponse("{\"id\":42}")
            : JsonResponse("{\"page\":1,\"totalPages\":1,\"totalResults\":0,\"results\":[]}"));
        var newService = CreateService(newDb, newHandler, protection, new SeerrSettings());

        await newService.SearchAsync(user.Id, "Dune", 1, CancellationToken.None);

        Assert.Equal(2, newHandler.Requests.Count);
        Assert.Null(newHandler.Requests[0].SeerrUserId);
        Assert.Equal("42", newHandler.Requests[1].SeerrUserId);
    }

    [Fact]
    public async Task EnvironmentBootstrapNeverOverwritesAnExistingDatabaseRecord()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        fixture.Db.Integrations.Add(new Integration
        {
            Type = IntegrationType.Seerr,
            Name = "Seerr",
            BaseUrl = "http://database-seerr:5055",
            Enabled = false,
        });
        await fixture.Db.SaveChangesAsync();
        var service = CreateService(
            fixture,
            new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called")),
            new EphemeralDataProtectionProvider(),
            new SeerrSettings
            {
                BaseUrl = "http://environment-seerr:5055",
                PublicUrl = "https://seerr.example.test",
                ApiKey = "environment-api-key",
            });

        await service.EnsureBootstrapConfigAsync(CancellationToken.None);

        var integration = Assert.Single(fixture.Db.Integrations);
        Assert.Equal("http://database-seerr:5055", integration.BaseUrl);
        Assert.Empty(fixture.Db.IntegrationSecrets);
    }

    private static async Task<SeerrService> CreateConfiguredServiceAsync(
        UserSettingsServiceTests.TestDb fixture,
        HttpMessageHandler handler)
    {
        var protection = new EphemeralDataProtectionProvider();
        var integration = new Integration
        {
            Type = IntegrationType.Seerr,
            Name = "Seerr",
            BaseUrl = "http://seerr:5055",
            OpenUrl = "https://seerr.example.test",
            Enabled = true,
        };
        integration.Secrets.Add(new IntegrationSecret
        {
            SecretKey = "api-key",
            ProtectedValue = protection.CreateProtector("Household.Seerr.ApiKey.v1").Protect("test-api-key"),
        });
        fixture.Db.Integrations.Add(integration);
        await fixture.Db.SaveChangesAsync();
        return CreateService(fixture, handler, protection, new SeerrSettings());
    }

    private static SeerrService CreateService(
        UserSettingsServiceTests.TestDb fixture,
        HttpMessageHandler handler,
        IDataProtectionProvider protection,
        SeerrSettings settings) =>
        CreateService(fixture.Db, handler, protection, settings);

    private static SeerrService CreateService(
        Household.Api.Data.AppDbContext db,
        HttpMessageHandler handler,
        IDataProtectionProvider protection,
        SeerrSettings settings) =>
        new(
            db,
            new HttpClient(handler),
            protection,
            Options.Create(settings),
            NullLogger<SeerrService>.Instance);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("X-API-User", out var values);
            Requests.Add(new RequestRecord(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                values?.SingleOrDefault()));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RequestRecord(HttpMethod Method, string PathAndQuery, string? SeerrUserId);
}
