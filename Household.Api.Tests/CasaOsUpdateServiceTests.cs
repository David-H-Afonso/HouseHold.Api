using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Application.Services;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Household.Api.Infrastructure.Integrations.CasaOs;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class CasaOsUpdateServiceTests
{
    private const string RawToken = "eyJhbGciOiJFUzI1NiJ9.eyJpZCI6MX0.valid-signature";
    private const string RawRefreshToken = "eyJhbGciOiJFUzI1NiJ9.eyJpc3MiOiJyZWZyZXNoIn0.initial-refresh";
    private static readonly byte[] HouseholdYaml = Encoding.UTF8.GetBytes(
        "name: household\nservices:\n  api:\n    image: ghcr.io/example/household-api:latest\n    environment:\n      PRIVATE_VALUE: server-secret\n"
    );

    [Fact]
    public async Task Config_IsPurposeProtectedAndResponseDoesNotExposeTokenOrInternalUrl()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        using var temp = TempDirectory.Create();
        var service = CreateService(fixture, new RecordingHandler(_ => JsonResponse("{}")), protection, temp.Path);

        var result = await service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest("http://casaos-host.lan:81/internal", RawToken),
            CancellationToken.None
        );
        var stored = fixture.Db.IntegrationSecrets.Single();
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.True(result.Configured);
        Assert.True(result.HasToken);
        Assert.DoesNotContain(RawToken, stored.ProtectedValue);
        Assert.DoesNotContain(RawToken, serialized);
        Assert.DoesNotContain("casaos-host", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.ThrowsAny<Exception>(() =>
            protection.CreateProtector("Household.IntegrationSecrets.v1").Unprotect(stored.ProtectedValue)
        );

        var genericIntegrations = new IntegrationService(fixture.Db, new SecretProtector(protection));
        Assert.Empty(await genericIntegrations.GetAllAsync(CancellationToken.None));
        Assert.Null(await genericIntegrations.GetByIdAsync(stored.IntegrationId, CancellationToken.None));
    }

    [Fact]
    public async Task Config_WithRefreshToken_ImmediatelyRotatesAndPersistsLatestPair()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        using var temp = TempDirectory.Create();
        var refreshCount = 0;
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/root/v1/users/refresh", request.RequestUri?.PathAndQuery);
            refreshCount++;
            return JsonResponse($"{{\"success\":200,\"message\":\"ok\",\"data\":{{\"access_token\":\"header.access-{refreshCount}.signature\",\"refresh_token\":\"header.refresh-{refreshCount}.signature\",\"expires_at\":4102444800}}}}");
        });
        var service = CreateService(fixture, handler, protection, temp.Path);

        var result = await service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest(
                "http://casaos-host.lan:81/root",
                RawToken,
                RawRefreshToken
            ),
            CancellationToken.None
        );
        var refreshedAgain = await service.RefreshTokenAsync(CancellationToken.None);

        Assert.True(result.Configured);
        Assert.True(result.HasRefreshToken);
        Assert.True(refreshedAgain);
        Assert.Equal(2, refreshCount);
        Assert.Contains(RawRefreshToken, Encoding.UTF8.GetString(handler.Requests[0].Body!));
        Assert.Contains("header.refresh-1.signature", Encoding.UTF8.GetString(handler.Requests[1].Body!));

        var protector = protection.CreateProtector("Household.CasaOS.RawJwt.v1");
        var secrets = fixture.Db.IntegrationSecrets.ToDictionary(item => item.SecretKey, item => item.ProtectedValue);
        Assert.Equal("header.access-2.signature", protector.Unprotect(secrets[CasaOsUpdatePolicy.TokenSecretKey]));
        Assert.Equal("header.refresh-2.signature", protector.Unprotect(secrets[CasaOsUpdatePolicy.RefreshTokenSecretKey]));
    }

    [Fact]
    public async Task Config_WithRefreshToken_ReplacesAnExistingAccessSecret()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"success\":200,\"message\":\"ok\",\"data\":{\"access_token\":\"header.access.signature\",\"refresh_token\":\"header.refresh.signature\",\"expires_at\":4102444800}}"
        ));
        var service = CreateService(fixture, handler, protection, temp.Path);

        await service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest("http://casaos-host.lan:81/root", RawToken),
            CancellationToken.None
        );
        var result = await service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest("http://casaos-host.lan:81/root", RawToken, RawRefreshToken),
            CancellationToken.None
        );

        Assert.True(result.HasRefreshToken);
        Assert.Equal(2, fixture.Db.IntegrationSecrets.Count());
    }

    [Fact]
    public async Task Config_WithRejectedRefreshToken_PreservesExistingConfiguration()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var protection = new EphemeralDataProtectionProvider();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"success\":20006}", Encoding.UTF8, "application/json"),
        });
        var service = CreateService(fixture, handler, protection, temp.Path);
        await service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest("http://original-casaos.lan", RawToken),
            CancellationToken.None
        );

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => service.UpdateConfigAsync(
            new UpdateCasaOsUpdateConfigRequest(
                "http://replacement-casaos.lan",
                "header.replacement.signature",
                RawRefreshToken
            ),
            CancellationToken.None
        ));

        Assert.Equal("casaos_token_pair_invalid", exception.Code);
        Assert.Equal("http://original-casaos.lan", fixture.Db.Integrations.Single().BaseUrl);
        Assert.Single(fixture.Db.IntegrationSecrets);
    }

    [Theory]
    [InlineData("Household")]
    [InlineData("../household")]
    [InlineData("household/other")]
    [InlineData("jellyseerr")]
    public async Task Update_RejectsAnythingOutsideExactAllowlist(string appId)
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.QueueUpdateAsync(
            Guid.NewGuid(), appId, $"UPDATE {appId}", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Update_RejectsMismatchedConfirmationBeforeHttpOrAudit()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueueUpdateAsync(
            Guid.NewGuid(), "household", "update household", CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.Empty(fixture.Db.IntegrationActionLogs);
    }

    [Theory]
    [InlineData("../backup")]
    [InlineData("%2e%2e%2fbackup")]
    [InlineData("20260724T1200000000000Z-aaaaaaaaaaaaaaaa.yml")]
    [InlineData("C:\\backup")]
    public async Task Rollback_RejectsCallerControlledBackupPaths(string backupId)
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called"));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);

        await Assert.ThrowsAsync<ArgumentException>(() => service.QueueRollbackAsync(
            Guid.NewGuid(), "household", "ROLLBACK household", backupId, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Update_RefusesRedirectAndDoesNotFollowIt()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://attacker.example/steal") },
        });
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);
        await ConfigureAsync(service);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => service.QueueUpdateAsync(
            Guid.NewGuid(), "household", "UPDATE household", CancellationToken.None));

        Assert.Equal("casaos_redirect_refused", exception.Code);
        Assert.Single(handler.Requests);
        Assert.Equal(IntegrationActionStatus.Failed, fixture.Db.IntegrationActionLogs.Single().Status);
    }

    [Fact]
    public async Task Update_RejectsYamlOverConfiguredLimitBeforePut()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var oversized = new byte[65 * 1024];
        Array.Fill(oversized, (byte)'x');
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(oversized) { Headers = { ContentType = new MediaTypeHeaderValue("application/yaml") } },
        });
        var service = CreateService(
            fixture,
            handler,
            new EphemeralDataProtectionProvider(),
            temp.Path,
            maxYamlBytes: 64 * 1024
        );
        await ConfigureAsync(service);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => service.QueueUpdateAsync(
            Guid.NewGuid(), "household", "UPDATE household", CancellationToken.None));

        Assert.Equal("casaos_response_too_large", exception.Code);
        Assert.Single(handler.Requests);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Update_SendsExactYamlWithRawAuthorizationAndAuditsQueuedAcceptance()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("admin@example.test");
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? YamlResponse(HouseholdYaml)
            : JsonResponse("{\"message\":\"accepted\"}"));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);
        await ConfigureAsync(service);

        var result = await service.QueueUpdateAsync(user.Id, "household", "UPDATE household", CancellationToken.None);

        Assert.Equal(IntegrationActionStatus.Queued, result.Status);
        Assert.Contains("not been verified", result.Message);
        Assert.DoesNotContain(temp.Path, result.BackupId);
        Assert.Matches(@"^\d{8}T\d{13}Z-[a-f0-9]{16}$", result.BackupId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("application/yaml", handler.Requests[0].Accept);
        Assert.Equal(RawToken, handler.Requests[0].Authorization);
        Assert.Equal(RawToken, handler.Requests[1].Authorization);
        Assert.Equal("application/yaml", handler.Requests[1].ContentType);
        Assert.Equal(HouseholdYaml, handler.Requests[1].Body);
        Assert.Equal(
            "/root/v2/app_management/compose/household?dry_run=false&check_port_conflict=true",
            handler.Requests[1].PathAndQuery
        );
        var audit = fixture.Db.IntegrationActionLogs.Single();
        Assert.Equal(user.Id, audit.UserId);
        Assert.Equal(CasaOsUpdatePolicy.UpdateAction, audit.Action);
        Assert.Equal(IntegrationActionStatus.Queued, audit.Status);
        Assert.DoesNotContain("server-secret", audit.ResultSummaryJson ?? string.Empty);
        Assert.Contains("household-api:latest", audit.ResultSummaryJson);
        var backupPath = Path.Combine(temp.Path, "household", $"{result.BackupId}.yml");
        Assert.Equal(HouseholdYaml, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task Rollback_PutsSelectedBackupUnchangedAndCreatesSafetyBackup()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("admin@example.test");
        using var temp = TempDirectory.Create();
        var getCount = 0;
        var firstHandler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? YamlResponse(++getCount == 1
                ? HouseholdYaml
                : Encoding.UTF8.GetBytes("name: household\nservices:\n  api:\n    image: ghcr.io/example/household-api:new\n"))
            : JsonResponse("{\"message\":\"accepted\"}"));
        var protection = new EphemeralDataProtectionProvider();
        var service = CreateService(fixture, firstHandler, protection, temp.Path);
        await ConfigureAsync(service);
        var update = await service.QueueUpdateAsync(user.Id, "household", "UPDATE household", CancellationToken.None);

        var rollback = await service.QueueRollbackAsync(
            user.Id,
            "household",
            "ROLLBACK household",
            update.BackupId,
            CancellationToken.None
        );

        var rollbackPut = firstHandler.Requests.Last(request => request.Method == HttpMethod.Put);
        Assert.Equal(HouseholdYaml, rollbackPut.Body);
        Assert.Equal(update.BackupId, rollback.BackupId);
        Assert.NotNull(rollback.SafetyBackupId);
        Assert.NotEqual(update.BackupId, rollback.SafetyBackupId);
        Assert.Equal(2, fixture.Db.IntegrationActionLogs.Count());
        Assert.All(fixture.Db.IntegrationActionLogs, item => Assert.Equal(IntegrationActionStatus.Queued, item.Status));
        var history = await service.GetHistoryAsync("household", CancellationToken.None);
        Assert.Equal(CasaOsUpdatePolicy.RollbackAction, history[0].Action);
        Assert.Equal(update.BackupId, history[0].BackupId);
        Assert.Equal(rollback.SafetyBackupId, history[0].SafetyBackupId);
    }

    [Fact]
    public async Task UpdateAvailability_UsesOnlyWellFormedCasaOsContractOtherwiseUnknown()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var handler = new RecordingHandler(_ => JsonResponse(
            "{\"data\":[{\"store_app_id\":\"household\",\"status\":\"idle\"}]}"
        ));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);
        await ConfigureAsync(service);

        var parsed = await service.GetAppCapabilitiesAsync(CancellationToken.None);
        Assert.True(parsed.UpdateAvailability["household"]);
        Assert.False(parsed.UpdateAvailability["jellyfin"]);

        handler.ResponseFactory = _ => JsonResponse("{\"data\":[{\"unexpected\":true}]}");
        var unknown = await service.GetAppCapabilitiesAsync(CancellationToken.None);
        Assert.All(unknown.UpdateAvailability.Values, value => Assert.Null(value));
    }

    [Fact]
    public async Task UpdateConfig_RejectsLoopbackOrUrlComponentsThatCouldBypassConfiguredTarget()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        using var temp = TempDirectory.Create();
        var service = CreateService(
            fixture,
            new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called")),
            new EphemeralDataProtectionProvider(),
            temp.Path
        );

        foreach (var url in new[]
                 {
                     "http://127.0.0.1",
                     "http://localhost:81",
                     "http://user@casaos-host.lan",
                     "http://casaos-host.lan?target=other",
                     "http://casaos-host.lan/#fragment",
                 })
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateConfigAsync(
                new UpdateCasaOsUpdateConfigRequest(url, RawToken), CancellationToken.None));
    }

    [Fact]
    public async Task Update_AcceptsExplicitProjectMarkerWhenCasaOsYamlOmitsName()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("admin@example.test");
        using var temp = TempDirectory.Create();
        var yaml = Encoding.UTF8.GetBytes(
            "services:\n  api:\n    image: ghcr.io/example/household-api:latest\nx-household-compose-project: household\n"
        );
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? YamlResponse(yaml)
            : JsonResponse("{\"message\":\"accepted\"}"));
        var service = CreateService(fixture, handler, new EphemeralDataProtectionProvider(), temp.Path);
        await ConfigureAsync(service);

        var result = await service.QueueUpdateAsync(user.Id, "household", "UPDATE household", CancellationToken.None);

        Assert.Equal(IntegrationActionStatus.Queued, result.Status);
        Assert.Equal(yaml, handler.Requests.Single(request => request.Method == HttpMethod.Put).Body);
    }

    [Fact]
    public async Task SameAppConcurrentUpdate_IsRejectedBeforeSecondCasaOsRequest()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("admin@example.test");
        using var temp = TempDirectory.Create();
        var getStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGet = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AsyncRecordingHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                getStarted.SetResult();
                await releaseGet.Task;
                return YamlResponse(HouseholdYaml);
            }
            return JsonResponse("{\"message\":\"accepted\"}");
        });
        var locks = new CasaOsUpdateLocks();
        var protection = new EphemeralDataProtectionProvider();
        var firstService = CreateService(fixture, handler, protection, temp.Path, locks: locks);
        var secondService = CreateService(fixture, handler, protection, temp.Path, locks: locks);
        await ConfigureAsync(firstService);

        var first = firstService.QueueUpdateAsync(user.Id, "household", "UPDATE household", CancellationToken.None);
        await getStarted.Task;
        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => secondService.QueueUpdateAsync(
            user.Id, "household", "UPDATE household", CancellationToken.None));
        releaseGet.SetResult();
        await first;

        Assert.Equal("casaos_operation_in_progress", exception.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static CasaOsUpdateService CreateService(
        UserSettingsServiceTests.TestDb fixture,
        HttpMessageHandler handler,
        IDataProtectionProvider protection,
        string backupRoot,
        int maxYamlBytes = 2 * 1024 * 1024,
        CasaOsUpdateLocks? locks = null
    ) => new(
        fixture.Db,
        new HttpClient(handler),
        protection,
        Options.Create(new CasaOsUpdateSettings
        {
            BackupRoot = backupRoot,
            RequestTimeoutSeconds = 5,
            MaxYamlBytes = maxYamlBytes,
            MaxJsonBytes = 64 * 1024,
        }),
        locks ?? new CasaOsUpdateLocks(),
        NullLogger<CasaOsUpdateService>.Instance
    );

    private static Task ConfigureAsync(CasaOsUpdateService service) => service.UpdateConfigAsync(
        new UpdateCasaOsUpdateConfigRequest("http://casaos-host.lan:81/root", RawToken),
        CancellationToken.None
    );

    private static HttpResponseMessage YamlResponse(byte[] yaml) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(yaml) { Headers = { ContentType = new MediaTypeHeaderValue("application/yaml") } },
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory
    ) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = responseFactory;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.PathAndQuery,
                request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null,
                string.Join(",", request.Headers.Accept.Select(value => value.MediaType)),
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken)
            ));
            return ResponseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string? Authorization,
        string Accept,
        string? ContentType,
        byte[]? Body
    );

    private sealed class AsyncRecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory
    ) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return await responseFactory(request);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        private TempDirectory(string path) => Path = path;

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"household-casaos-tests-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
