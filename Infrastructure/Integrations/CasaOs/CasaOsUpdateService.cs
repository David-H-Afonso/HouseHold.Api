using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.CasaOs;

public sealed class CasaOsUpdateService : ICasaOsUpdateService
{
    private const int HistoryLimit = 50;
    private const int MaxAuditImages = 8;
    private const int MaxAuditImageLength = 300;
    private static readonly SemaphoreSlim TokenRefreshLock = new(1, 1);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _appLocks;
    private static readonly Regex RawJwtPattern = new(
        @"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );
    private static readonly Regex BackupIdPattern = new(
        @"^\d{8}T\d{13}Z-[a-f0-9]{16}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );
    private static readonly Regex SafeImagePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._/@:-]{0,299}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    );
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
    };

    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _tokenProtector;
    private readonly ILogger<CasaOsUpdateService> _logger;
    private readonly string _backupRoot;
    private readonly int _maxYamlBytes;
    private readonly int _maxJsonBytes;

    public CasaOsUpdateService(
        AppDbContext db,
        HttpClient httpClient,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<CasaOsUpdateSettings> settings,
        CasaOsUpdateLocks appLocks,
        ILogger<CasaOsUpdateService> logger
    )
    {
        _db = db;
        _httpClient = httpClient;
        _tokenProtector = dataProtectionProvider.CreateProtector("Household.CasaOS.RawJwt.v1");
        _appLocks = appLocks.Locks;
        _logger = logger;

        var configured = settings.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(configured.RequestTimeoutSeconds, 5, 30));
        _maxYamlBytes = Math.Clamp(configured.MaxYamlBytes, 64 * 1024, 8 * 1024 * 1024);
        _maxJsonBytes = Math.Clamp(configured.MaxJsonBytes, 16 * 1024, 1024 * 1024);
        var configuredBackupRoot = string.IsNullOrWhiteSpace(configured.BackupRoot)
            ? "/data/compose-backups"
            : configured.BackupRoot;
        if (!Path.IsPathRooted(configuredBackupRoot))
            throw new ArgumentException("CasaOS compose backup root must be an absolute path.");
        var fullBackupRoot = Path.GetFullPath(configuredBackupRoot);
        if (string.Equals(
                fullBackupRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetPathRoot(fullBackupRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
            ))
            throw new ArgumentException("CasaOS compose backup root cannot be a filesystem root.");
        _backupRoot = fullBackupRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public async Task<CasaOsUpdateConfigDto> GetConfigAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        return ToConfigDto(integration);
    }

    public async Task<CasaOsUpdateConfigDto> UpdateConfigAsync(
        UpdateCasaOsUpdateConfigRequest request,
        CancellationToken cancellationToken
    )
    {
        await TokenRefreshLock.WaitAsync(cancellationToken);

        try
        {
            return await UpdateConfigCoreAsync(request, cancellationToken);
        }
        finally
        {
            TokenRefreshLock.Release();
        }
    }

    private async Task<CasaOsUpdateConfigDto> UpdateConfigCoreAsync(
        UpdateCasaOsUpdateConfigRequest request,
        CancellationToken cancellationToken
    )
    {
        var baseUrl = NormalizeBaseUrl(request.InternalBaseUrl);
        var existing = await _db.Integrations.AsNoTracking()
            .Where(item => item.Type == IntegrationType.CasaOS && item.Name == CasaOsUpdatePolicy.IntegrationName)
            .Select(item => new
            {
                item.BaseUrl,
                HasAccessToken = item.Secrets.Any(secret => secret.SecretKey == CasaOsUpdatePolicy.TokenSecretKey),
                HasRefreshToken = item.Secrets.Any(secret => secret.SecretKey == CasaOsUpdatePolicy.RefreshTokenSecretKey),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (existing?.HasAccessToken == true
            && !string.IsNullOrWhiteSpace(existing.BaseUrl)
            && !HasSameAuthority(existing.BaseUrl, baseUrl)
            && (string.IsNullOrWhiteSpace(request.RawToken)
                || existing.HasRefreshToken && string.IsNullOrWhiteSpace(request.RawRefreshToken)))
            throw new ArgumentException("Provide fresh CasaOS credentials when changing servers.");
        var accessToken = string.IsNullOrEmpty(request.RawToken) ? null : NormalizeRawJwt(request.RawToken);
        var refreshToken = string.IsNullOrEmpty(request.RawRefreshToken) ? null : NormalizeRawJwt(request.RawRefreshToken);
        if (refreshToken is not null)
        {
            TokenPair? rotated;
            try
            {
                rotated = await RequestTokenPairAsync(baseUrl, refreshToken, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException or ArgumentException)
            {
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    "CasaOS could not validate the token pair.",
                    "casaos_unavailable"
                );
            }

            if (rotated is null)
                throw new IntegrationGatewayException(
                    HttpStatusCode.Conflict,
                    "CasaOS rejected the access and refresh token pair. Copy both fresh tokens from the same CasaOS session.",
                    "casaos_token_pair_invalid"
                );
            accessToken = rotated.AccessToken;
            refreshToken = rotated.RefreshToken;
        }

        return await PersistConfigAsync(baseUrl, accessToken, refreshToken, cancellationToken);
    }

    private async Task<CasaOsUpdateConfigDto> PersistConfigAsync(
        string baseUrl,
        string? accessToken,
        string? refreshToken,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _db.ChangeTracker.Clear();
                var integrationId = await _db.Integrations
                    .AsNoTracking()
                    .Where(item => item.Type == IntegrationType.CasaOS && item.Name == CasaOsUpdatePolicy.IntegrationName)
                    .Select(item => (Guid?)item.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (integrationId is null)
                {
                    var integration = new Integration
                    {
                        Type = IntegrationType.CasaOS,
                        Name = CasaOsUpdatePolicy.IntegrationName,
                        BaseUrl = baseUrl,
                        Enabled = true,
                    };
                    _db.Integrations.Add(integration);
                    await _db.SaveChangesAsync(cancellationToken);
                    integrationId = integration.Id;
                }
                else
                {
                    await _db.Integrations
                        .Where(item => item.Id == integrationId.Value)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.BaseUrl, baseUrl)
                            .SetProperty(item => item.OpenUrl, (string?)null)
                            .SetProperty(item => item.Enabled, true)
                            .SetProperty(item => item.UpdatedAt, DateTime.UtcNow), cancellationToken);
                }

                await ReplaceSecretsAsync(integrationId.Value, accessToken, refreshToken, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return ToConfigDto(await LoadIntegrationAsync(cancellationToken));
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                _logger.LogWarning("CasaOS integration changed while replacing its token pair; retrying with fresh data");
                _db.ChangeTracker.Clear();
            }
        }

        throw new DbUpdateConcurrencyException("CasaOS integration changed while saving its token pair.");
    }

    private async Task ReplaceSecretsAsync(
        Guid integrationId,
        string? accessToken,
        string? refreshToken,
        CancellationToken cancellationToken
    )
    {
        var secretsToPersist = new[]
        {
            (Key: CasaOsUpdatePolicy.TokenSecretKey, Value: accessToken),
            (Key: CasaOsUpdatePolicy.RefreshTokenSecretKey, Value: refreshToken),
        };
        foreach (var (key, value) in secretsToPersist)
        {
            if (value is null)
                continue;

            await _db.IntegrationSecrets
                .Where(secret => secret.IntegrationId == integrationId && secret.SecretKey == key)
                .ExecuteDeleteAsync(cancellationToken);
            _db.IntegrationSecrets.Add(new IntegrationSecret
            {
                IntegrationId = integrationId,
                SecretKey = key,
                ProtectedValue = _tokenProtector.Protect(value),
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return connection is not null && await TryRefreshConnectionAsync(connection, cancellationToken);
    }

    public async Task<CasaOsAppCapabilities> GetAppCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var availability = CasaOsUpdatePolicy.AppIds.ToDictionary(appId => appId, _ => (bool?)null, StringComparer.Ordinal);
        var connection = await GetConnectionAsync(cancellationToken);
        if (connection is null)
            return new CasaOsAppCapabilities(false, availability);

        try
        {
            using var response = await SendAsync(() =>
            {
                var request = CreateRequest(HttpMethod.Get, connection, "v2/app_management/apps/upgradable");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return request;
            }, connection, cancellationToken);
            if (IsRedirect(response.StatusCode) || response.StatusCode != HttpStatusCode.OK)
                return new CasaOsAppCapabilities(true, availability);

            if (!IsJsonContentType(response.Content.Headers.ContentType?.MediaType))
                return new CasaOsAppCapabilities(true, availability);

            var content = await ReadBoundedAsync(response.Content, _maxJsonBytes, "CasaOS JSON response", cancellationToken);
            if (!TryParseUpgradableApps(content, out var upgradableApps))
                return new CasaOsAppCapabilities(true, availability);

            foreach (var appId in availability.Keys.ToList())
                availability[appId] = upgradableApps.Contains(CasaOsUpdatePolicy.GetProjectName(appId));

            return new CasaOsAppCapabilities(true, availability);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IntegrationGatewayException
        )
        {
            _logger.LogWarning(
                "CasaOS update availability is unknown because its response was unavailable or invalid ({ErrorType})",
                exception.GetType().Name
            );
            return new CasaOsAppCapabilities(true, availability);
        }
    }

    public async Task<CasaOsQueuedOperationDto> QueueUpdateAsync(
        Guid actorUserId,
        string appId,
        CancellationToken cancellationToken
    )
    {
        RequireAllowedAppId(appId);
        var projectName = CasaOsUpdatePolicy.GetProjectName(appId);

        var gate = await AcquireAppLockAsync(appId, cancellationToken);
        try
        {
            var connection = await RequireConnectionAsync(cancellationToken);
            var actionLog = await StartActionAsync(
                actorUserId,
                connection.IntegrationId,
                appId,
                CasaOsUpdatePolicy.UpdateAction,
                cancellationToken
            );
            var accepted = false;
            try
            {
                var currentYaml = await GetCurrentYamlAsync(connection, projectName, cancellationToken);
                var previousImages = ValidateComposeYaml(currentYaml, projectName);
                var backupId = await WriteBackupAsync(appId, currentYaml, cancellationToken);

                actionLog.ResultSummaryJson = SerializeAudit(new { backupId, projectName, previousImages });
                await _db.SaveChangesAsync(cancellationToken);

                await PutComposeAsync(
                    connection,
                    projectName,
                    NormalizeComposeImagesToLatest(currentYaml),
                    cancellationToken
                );
                accepted = true;
                actionLog.Status = IntegrationActionStatus.Queued;
                await _db.SaveChangesAsync(cancellationToken);

                return ToQueuedDto(actionLog, backupId, null);
            }
            catch (Exception exception) when (!accepted)
            {
                await TryMarkFailedAsync(actionLog, GetSafeErrorCode(exception));
                throw NormalizeOperationException(exception);
            }
            catch (Exception)
            {
                _logger.LogError(
                    "CasaOS accepted action {ActionLogId} for {AppId}, but its queued audit status could not be persisted",
                    actionLog.Id,
                    appId
                );
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    "CasaOS accepted the request, but Household could not persist its final queued status.",
                    "casaos_acceptance_audit_uncertain"
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CasaOsQueuedOperationDto> QueueRollbackAsync(
        Guid actorUserId,
        string appId,
        string confirmation,
        string? backupId,
        CancellationToken cancellationToken
    )
    {
        RequireAllowedAppId(appId);
        RequireConfirmation(confirmation, $"ROLLBACK {appId}");
        if (backupId is not null)
            RequireValidBackupId(backupId);
        var projectName = CasaOsUpdatePolicy.GetProjectName(appId);

        var gate = await AcquireAppLockAsync(appId, cancellationToken);
        try
        {
            var connection = await RequireConnectionAsync(cancellationToken);
            var actionLog = await StartActionAsync(
                actorUserId,
                connection.IntegrationId,
                appId,
                CasaOsUpdatePolicy.RollbackAction,
                cancellationToken
            );
            var accepted = false;
            try
            {
                var selectedBackupId = backupId ?? FindLatestBackupId(appId);
                var backupYaml = await ReadBackupAsync(appId, selectedBackupId, cancellationToken);
                var previousImages = ValidateComposeYaml(backupYaml, projectName);

                var currentYaml = await GetCurrentYamlAsync(connection, projectName, cancellationToken);
                var currentImages = ValidateComposeYaml(currentYaml, projectName);
                var safetyBackupId = await WriteBackupAsync(appId, currentYaml, cancellationToken);

                actionLog.ResultSummaryJson = SerializeAudit(new
                {
                    backupId = selectedBackupId,
                    safetyBackupId,
                    projectName,
                    previousImages,
                    currentImages,
                });
                await _db.SaveChangesAsync(cancellationToken);

                await PutComposeAsync(connection, projectName, backupYaml, cancellationToken);
                accepted = true;
                actionLog.Status = IntegrationActionStatus.Queued;
                await _db.SaveChangesAsync(cancellationToken);

                return ToQueuedDto(actionLog, selectedBackupId, safetyBackupId);
            }
            catch (Exception exception) when (!accepted)
            {
                await TryMarkFailedAsync(actionLog, GetSafeErrorCode(exception));
                throw NormalizeOperationException(exception);
            }
            catch (Exception)
            {
                _logger.LogError(
                    "CasaOS accepted rollback {ActionLogId} for {AppId}, but its queued audit status could not be persisted",
                    actionLog.Id,
                    appId
                );
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    "CasaOS accepted the rollback, but Household could not persist its final queued status.",
                    "casaos_acceptance_audit_uncertain"
                );
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CasaOsActionStatusDto>> GetHistoryAsync(
        string appId,
        CancellationToken cancellationToken
    )
    {
        RequireAllowedAppId(appId);
        var logs = await _db
            .IntegrationActionLogs.AsNoTracking()
            .Where(log =>
                log.AppId == appId
                && (log.Action == CasaOsUpdatePolicy.UpdateAction || log.Action == CasaOsUpdatePolicy.RollbackAction)
            )
            .OrderByDescending(log => log.StartedAt)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);
        return logs.Select(ToStatusDto).ToList();
    }

    public async Task<CasaOsActionStatusDto?> GetStatusAsync(
        string appId,
        Guid actionLogId,
        CancellationToken cancellationToken
    )
    {
        RequireAllowedAppId(appId);
        var log = await _db.IntegrationActionLogs.AsNoTracking().SingleOrDefaultAsync(
            item =>
                item.Id == actionLogId
                && item.AppId == appId
                && (item.Action == CasaOsUpdatePolicy.UpdateAction || item.Action == CasaOsUpdatePolicy.RollbackAction),
            cancellationToken
        );
        return log is null ? null : ToStatusDto(log);
    }

    private async Task<IntegrationActionLog> StartActionAsync(
        Guid actorUserId,
        Guid integrationId,
        string appId,
        string action,
        CancellationToken cancellationToken
    )
    {
        var log = new IntegrationActionLog
        {
            UserId = actorUserId,
            IntegrationId = integrationId,
            AppId = appId,
            Action = action,
            Status = IntegrationActionStatus.Running,
            Source = "Household.CasaOS",
            RequestSummaryJson = SerializeAudit(new { appId, action }),
        };
        _db.IntegrationActionLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
        return log;
    }

    private async Task TryMarkFailedAsync(IntegrationActionLog log, string errorCode)
    {
        try
        {
            log.Status = IntegrationActionStatus.Failed;
            log.ErrorMessage = errorCode;
            log.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            _logger.LogError("Could not persist failed CasaOS action status for {ActionLogId}", log.Id);
        }
    }

    private async Task<byte[]> GetCurrentYamlAsync(
        Connection connection,
        string appId,
        CancellationToken cancellationToken
    )
    {
        var escapedAppId = Uri.EscapeDataString(appId);
        using var response = await SendAsync(() =>
        {
            var request = CreateRequest(
                HttpMethod.Get,
                connection,
                $"v2/app_management/compose/{escapedAppId}"
            );
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/yaml"));
            return request;
        }, connection, cancellationToken);
        RequireCasaOsOk(response, "fetch compose YAML");
        return await ReadBoundedAsync(response.Content, _maxYamlBytes, "CasaOS YAML response", cancellationToken);
    }

    private async Task PutComposeAsync(
        Connection connection,
        string projectName,
        byte[] yaml,
        CancellationToken cancellationToken
    )
    {
        var escapedProjectName = Uri.EscapeDataString(projectName);
        using var response = await SendAsync(() =>
        {
            var request = CreateRequest(
                HttpMethod.Put,
                connection,
                $"v2/app_management/compose/{escapedProjectName}"
            );
            request.Content = new ByteArrayContent(yaml);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/yaml");
            return request;
        }, connection, cancellationToken);
        RequireCasaOsOk(response, "restore compose YAML");
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        Connection connection,
        CancellationToken cancellationToken
    )
    {
        HttpResponseMessage response;
        try
        {
            response = await SendHttpRequestAsync(requestFactory(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                "CasaOS is unavailable.",
                "casaos_unavailable"
            );
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized
            && await TryRefreshConnectionAsync(connection, cancellationToken))
        {
            response.Dispose();
            try
            {
                return await SendHttpRequestAsync(requestFactory(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    "CasaOS is unavailable.",
                    "casaos_unavailable"
                );
            }
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendHttpRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static void RequireCasaOsOk(HttpResponseMessage response, string operation)
    {
        if (IsRedirect(response.StatusCode))
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                "CasaOS redirects are refused for authenticated operations.",
                "casaos_redirect_refused"
            );

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "CasaOS authorization expired or is invalid. Reconnect CasaOS with a fresh JWT.",
                "casaos_reconnect_required"
            );

        if (!response.IsSuccessStatusCode)
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                $"CasaOS could not {operation}.",
                "casaos_request_rejected"
            );
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is >= 300 and <= 399;

    private static bool IsJsonContentType(string? mediaType) => mediaType is not null
        && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static HttpRequestMessage CreateRequest(HttpMethod method, Connection connection, string relativePath)
    {
        var requestUri = BuildSameOriginUri(connection.BaseUrl, relativePath);
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("Authorization", connection.RawToken);
        return request;
    }

    private static Uri BuildSameOriginUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || !Uri.TryCreate(baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/'), UriKind.Absolute, out var target)
            || !target.Scheme.Equals(baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !target.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != baseUri.Port
            || !string.IsNullOrEmpty(target.UserInfo))
            throw new InvalidOperationException("CasaOS request URI is invalid.");
        return target;
    }

    private async Task<Connection> RequireConnectionAsync(CancellationToken cancellationToken) =>
        await GetConnectionAsync(cancellationToken)
        ?? throw new IntegrationGatewayException(
            HttpStatusCode.Conflict,
            "CasaOS update operations are not configured.",
            "casaos_not_configured"
        );

    private async Task<bool> TryRefreshConnectionAsync(Connection connection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.RawRefreshToken))
            return false;

        await TokenRefreshLock.WaitAsync(cancellationToken);
        try
        {
            var latest = await GetConnectionAsync(cancellationToken);
            if (latest is null || string.IsNullOrWhiteSpace(latest.RawRefreshToken))
                return false;
            if (latest.IntegrationId != connection.IntegrationId
                || !HasSameAuthority(latest.BaseUrl, connection.BaseUrl))
                return false;

            var rotated = await RequestTokenPairAsync(latest.BaseUrl, latest.RawRefreshToken, cancellationToken);
            if (rotated is null)
                return false;
            var integration = await LoadIntegrationAsync(cancellationToken);
            if (integration is null)
                return false;

            var accessSecret = integration.Secrets.SingleOrDefault(item => item.SecretKey == CasaOsUpdatePolicy.TokenSecretKey);
            var refreshSecret = integration.Secrets.SingleOrDefault(item => item.SecretKey == CasaOsUpdatePolicy.RefreshTokenSecretKey);
            if (accessSecret is null || refreshSecret is null)
                return false;

            var integrationId = integration.Id;
            _db.ChangeTracker.Clear();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    await ReplaceSecretsAsync(integrationId, rotated.AccessToken, rotated.RefreshToken, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    break;
                }
                catch (DbUpdateException) when (attempt == 0)
                {
                    _logger.LogWarning("CasaOS token refresh changed while replacing its token pair; retrying");
                    _db.ChangeTracker.Clear();
                }
            }

            connection.RawToken = rotated.AccessToken;
            connection.RawRefreshToken = rotated.RefreshToken;
            return true;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or CryptographicException or ArgumentException or DbUpdateException)
        {
            _logger.LogWarning("CasaOS token refresh failed ({ErrorType})", exception.GetType().Name);
            return false;
        }
        finally
        {
            TokenRefreshLock.Release();
        }
    }

    private async Task<TokenPair?> RequestTokenPairAsync(
        string baseUrl,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildSameOriginUri(baseUrl, "v1/users/refresh"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { refresh_token = refreshToken }),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CasaOS token refresh was rejected with HTTP {StatusCode}", (int)response.StatusCode);
            return null;
        }

        var content = await ReadBoundedAsync(response.Content, _maxJsonBytes, "CasaOS refresh response", cancellationToken);
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("success", out var success)
            || !success.TryGetInt32(out var successCode)
            || successCode != 200)
            return null;
        var payload = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        var accessToken = ReadJsonString(payload, "access_token", "accessToken");
        var nextRefreshToken = ReadJsonString(payload, "refresh_token", "refreshToken");
        if (accessToken is null || nextRefreshToken is null)
        {
            _logger.LogWarning("CasaOS token refresh response did not contain both rotated tokens");
            return null;
        }

        return new TokenPair(NormalizeRawJwt(accessToken), NormalizeRawJwt(nextRefreshToken));
    }

    private static string? ReadJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                return property.GetString();
        }

        return null;
    }

    private async Task<Connection?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        return TryBuildConnection(integration, out var connection) ? connection : null;
    }

    private Task<Integration?> LoadIntegrationAsync(CancellationToken cancellationToken) =>
        _db
            .Integrations.AsNoTracking().Include(item => item.Secrets)
            .SingleOrDefaultAsync(
                item => item.Type == IntegrationType.CasaOS && item.Name == CasaOsUpdatePolicy.IntegrationName,
                cancellationToken
            );

    private CasaOsUpdateConfigDto ToConfigDto(Integration? integration)
    {
        var hasToken = integration?.Secrets.Any(item => item.SecretKey == CasaOsUpdatePolicy.TokenSecretKey) == true;
        var hasRefreshToken = integration?.Secrets.Any(item => item.SecretKey == CasaOsUpdatePolicy.RefreshTokenSecretKey) == true;
        return new CasaOsUpdateConfigDto(TryBuildConnection(integration, out _), hasToken, hasRefreshToken);
    }

    private bool TryBuildConnection(Integration? integration, out Connection? connection)
    {
        connection = null;
        var secret = integration?.Secrets.SingleOrDefault(item => item.SecretKey == CasaOsUpdatePolicy.TokenSecretKey);
        if (integration?.Enabled != true || string.IsNullOrWhiteSpace(integration.BaseUrl) || secret is null)
            return false;

        try
        {
            var baseUrl = NormalizeBaseUrl(integration.BaseUrl);
            var rawToken = NormalizeRawJwt(_tokenProtector.Unprotect(secret.ProtectedValue));
            var refreshSecret = integration.Secrets.SingleOrDefault(item => item.SecretKey == CasaOsUpdatePolicy.RefreshTokenSecretKey);
            var rawRefreshToken = refreshSecret is null ? null : NormalizeRawJwt(_tokenProtector.Unprotect(refreshSecret.ProtectedValue));
            connection = new Connection(integration.Id, baseUrl, rawToken, rawRefreshToken);
            return true;
        }
        catch (Exception exception) when (
            exception is CryptographicException or ArgumentException or FormatException
        )
        {
            return false;
        }
    }

    private static string NormalizeBaseUrl(string value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || IsLoopbackOrUnspecified(uri))
            throw new ArgumentException(
                "CasaOS internal base URL must be a non-loopback absolute HTTP(S) URL without credentials, query, or fragment."
            );

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
    }

    private static bool IsLoopbackOrUnspecified(Uri uri)
    {
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(uri.Host, out var address)
            && (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
    }

    private static bool HasSameAuthority(string first, string second)
    {
        if (!Uri.TryCreate(first, UriKind.Absolute, out var firstUri)
            || !Uri.TryCreate(second, UriKind.Absolute, out var secondUri))
            return false;
        return string.Equals(firstUri.Scheme, secondUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(firstUri.Host, secondUri.Host, StringComparison.OrdinalIgnoreCase)
            && firstUri.Port == secondUri.Port;
    }

    private static string NormalizeRawJwt(string value)
    {
        var token = value.Trim();
        if (token.Length is < 20 or > 16000 || !RawJwtPattern.IsMatch(token))
            throw new ArgumentException("CasaOS raw JWT is invalid.");
        return token;
    }

    private static void RequireAllowedAppId(string appId)
    {
        if (!CasaOsUpdatePolicy.IsAllowedAppId(appId))
            throw new KeyNotFoundException("CasaOS app is not allowlisted.");
    }

    private static void RequireConfirmation(string confirmation, string expected)
    {
        if (!string.Equals(confirmation, expected, StringComparison.Ordinal))
            throw new ArgumentException("Explicit CasaOS action confirmation does not match.");
    }

    private static void RequireValidBackupId(string backupId)
    {
        if (!BackupIdPattern.IsMatch(backupId))
            throw new ArgumentException("Backup ID is invalid.");
    }

    private static IReadOnlyList<string> ValidateComposeYaml(byte[] yaml, string appId)
    {
        if (yaml.Length == 0)
            throw InvalidComposeResponse();

        string text;
        try
        {
            text = StrictUtf8.GetString(yaml);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidComposeResponse();
        }

        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
            throw InvalidComposeResponse();

        var projectName = TryReadTopLevelScalar(text, "name");
        var extensionProjectName = TryReadTopLevelScalar(text, "x-household-compose-project");
        var hasServices = false;
        var images = new List<string>();
        var lines = text.Split('\n');
        var blockKeys = new Stack<(int Indent, string Key)>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;
            if (line[0] == '\t')
                throw InvalidComposeResponse();

            var indent = line.TakeWhile(character => character == ' ').Count();
            var trimmed = line[indent..];
            while (blockKeys.Count > 0 && blockKeys.Peek().Indent >= indent)
                blockKeys.Pop();

            var separator = trimmed.IndexOf(':');
            if (separator > 0)
            {
                var key = trimmed[..separator].Trim();
                var rawValue = trimmed[(separator + 1)..];
                if (key == "image"
                    && IsServiceImagePath(blockKeys)
                    && TryReadSimpleYamlScalar(rawValue, out var image)
                    && SafeImagePattern.IsMatch(image)
                    && !images.Contains(image, StringComparer.Ordinal)
                    && images.Count < MaxAuditImages)
                    images.Add(image[..Math.Min(image.Length, MaxAuditImageLength)]);

                if (string.IsNullOrWhiteSpace(rawValue) || rawValue.TrimStart().StartsWith('#'))
                    blockKeys.Push((indent, key));
            }

            if (indent == 0 && trimmed.TrimEnd().Equals("services:", StringComparison.Ordinal))
                hasServices = true;
        }

        var representsRequestedProject = string.Equals(projectName, appId, StringComparison.Ordinal)
            || (projectName is null && string.Equals(extensionProjectName, appId, StringComparison.Ordinal));
        if (!hasServices || !representsRequestedProject)
            throw InvalidComposeResponse();

        return images;
    }

    private static byte[] NormalizeComposeImagesToLatest(byte[] yaml)
    {
        var text = StrictUtf8.GetString(yaml);
        var lines = text.Split('\n');
        var blockKeys = new Stack<(int Indent, string Key)>();
        for (var index = 0; index < lines.Length; index++)
        {
            var rawLine = lines[index];
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var indent = line.TakeWhile(character => character == ' ').Count();
            var trimmed = line[indent..];
            while (blockKeys.Count > 0 && blockKeys.Peek().Indent >= indent)
                blockKeys.Pop();

            var separator = trimmed.IndexOf(':');
            if (separator > 0)
            {
                var key = trimmed[..separator].Trim();
                var rawValue = trimmed[(separator + 1)..];
                if (key == "image"
                    && IsServiceImagePath(blockKeys)
                    && TryReadSimpleYamlScalar(rawValue, out var image)
                    && SafeImagePattern.IsMatch(image))
                {
                    var latestImage = NormalizeImageTag(image);
                    if (!string.Equals(image, latestImage, StringComparison.Ordinal))
                    {
                        var lineEnding = rawLine.EndsWith('\r') ? "\r" : string.Empty;
                        lines[index] = ReplaceYamlScalar(line, separator, latestImage) + lineEnding;
                    }
                }

                if (string.IsNullOrWhiteSpace(rawValue) || rawValue.TrimStart().StartsWith('#'))
                    blockKeys.Push((indent, key));
            }
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    private static string ReplaceYamlScalar(string line, int separator, string value)
    {
        var rawValue = line[(separator + 1)..];
        var leadingWhitespaceLength = rawValue.TakeWhile(char.IsWhiteSpace).Count();
        var commentIndex = rawValue.IndexOf(" #", StringComparison.Ordinal);
        var comment = commentIndex >= 0 ? rawValue[commentIndex..] : string.Empty;
        return line[..(separator + 1)]
            + rawValue[..leadingWhitespaceLength]
            + value
            + comment;
    }

    private static string NormalizeImageTag(string image)
    {
        var digestSeparator = image.IndexOf('@');
        var withoutDigest = digestSeparator >= 0 ? image[..digestSeparator] : image;
        var lastSlash = withoutDigest.LastIndexOf('/');
        var lastColon = withoutDigest.LastIndexOf(':');
        return lastColon > lastSlash
            ? withoutDigest[..lastColon] + ":latest"
            : withoutDigest + ":latest";
    }

    private static bool IsServiceImagePath(Stack<(int Indent, string Key)> blockKeys)
    {
        var keys = blockKeys.Select(item => item.Key).Reverse().ToList();
        return keys.Count == 2 && keys[0] == "services" && keys[1].Length > 0;
    }

    private static string? TryReadTopLevelScalar(string yaml, string key)
    {
        var prefix = $"{key}:";
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0
                || line[0] is ' ' or '\t' or '#'
                || !line.StartsWith(prefix, StringComparison.Ordinal)
                || (line.Length > prefix.Length
                    && !char.IsWhiteSpace(line[prefix.Length])
                    && line[prefix.Length] != '#'))
                continue;
            return TryReadSimpleYamlScalar(line[prefix.Length..], out var value) ? value : null;
        }
        return null;
    }

    private static bool TryReadSimpleYamlScalar(string value, out string scalar)
    {
        scalar = value.Trim();
        if (scalar.Length == 0)
            return false;
        if (scalar.Length >= 2 && scalar[0] == '\'' && scalar[^1] == '\'')
        {
            scalar = scalar[1..^1].Replace("''", "'", StringComparison.Ordinal);
            return scalar.Length > 0;
        }
        if (scalar.Length >= 2 && scalar[0] == '"' && scalar[^1] == '"')
        {
            try
            {
                scalar = JsonSerializer.Deserialize<string>(scalar) ?? string.Empty;
                return scalar.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        var commentIndex = scalar.IndexOf(" #", StringComparison.Ordinal);
        if (commentIndex >= 0)
            scalar = scalar[..commentIndex].TrimEnd();
        return scalar.Length > 0;
    }

    private static IntegrationGatewayException InvalidComposeResponse() =>
        new(
            HttpStatusCode.BadGateway,
            "CasaOS returned compose YAML that could not be bound to the requested project.",
            "invalid_casaos_compose"
        );

    private async Task<string> WriteBackupAsync(
        string appId,
        byte[] yaml,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var appDirectory = EnsureBackupDirectory(appId);
            var backupId = $"{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture)}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
            var path = GetBackupPath(appDirectory, backupId);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 81920,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using var stream = new FileStream(path, options);
            await stream.WriteAsync(yaml, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return backupId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or NotSupportedException
        )
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.ServiceUnavailable,
                "CasaOS compose backup storage is unavailable.",
                "casaos_backup_unavailable"
            );
        }
    }

    private async Task<byte[]> ReadBackupAsync(
        string appId,
        string backupId,
        CancellationToken cancellationToken
    )
    {
        RequireValidBackupId(backupId);
        try
        {
            var appDirectory = EnsureBackupDirectory(appId);
            var path = GetBackupPath(appDirectory, backupId);
            if (!File.Exists(path))
                throw new KeyNotFoundException("CasaOS compose backup was not found.");
            EnsureNotReparsePoint(path);
            var fileLength = new FileInfo(path).Length;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            EnsureNotReparsePoint(path);
            return await ReadBoundedAsync(
                stream,
                fileLength,
                _maxYamlBytes,
                "CasaOS compose backup",
                cancellationToken
            );
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (IntegrationGatewayException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or NotSupportedException
        )
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.ServiceUnavailable,
                "CasaOS compose backup storage is unavailable.",
                "casaos_backup_unavailable"
            );
        }
    }

    private string FindLatestBackupId(string appId)
    {
        try
        {
            var appDirectory = EnsureBackupDirectory(appId);
            var backupId = Directory
                .EnumerateFiles(appDirectory, "*.yml", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(value => value is not null && BackupIdPattern.IsMatch(value))
                .OrderByDescending(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            return backupId ?? throw new KeyNotFoundException("No CasaOS compose backup is available.");
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException
        )
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.ServiceUnavailable,
                "CasaOS compose backup storage is unavailable.",
                "casaos_backup_unavailable"
            );
        }
    }

    private string EnsureBackupDirectory(string appId)
    {
        RequireAllowedAppId(appId);
        EnsureExistingPathHasNoReparsePoints(_backupRoot);
        CreatePrivateDirectory(_backupRoot);
        EnsureExistingPathHasNoReparsePoints(_backupRoot);

        var appDirectory = Path.GetFullPath(Path.Combine(_backupRoot, appId));
        EnsureConfined(appDirectory, _backupRoot);
        CreatePrivateDirectory(appDirectory);
        EnsureExistingPathHasNoReparsePoints(appDirectory);
        return appDirectory;
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (Directory.Exists(path))
            return;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }

    private static void EnsureExistingPathHasNoReparsePoints(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new IOException("Backup root is invalid.");
        var current = root;
        var relative = fullPath[root.Length..];
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries
                 ))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current))
                EnsureNotReparsePoint(current);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backup path cannot contain links.");
    }

    private static string GetBackupPath(string appDirectory, string backupId)
    {
        RequireValidBackupId(backupId);
        var path = Path.GetFullPath(Path.Combine(appDirectory, $"{backupId}.yml"));
        EnsureConfined(path, appDirectory);
        return path;
    }

    private static void EnsureConfined(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, comparison))
            throw new IOException("Backup path escapes its configured root.");
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        string description,
        CancellationToken cancellationToken
    )
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await ReadBoundedAsync(
            stream,
            content.Headers.ContentLength,
            maxBytes,
            description,
            cancellationToken
        );
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long? contentLength,
        int maxBytes,
        string description,
        CancellationToken cancellationToken
    )
    {
        if (contentLength > maxBytes)
            throw ResponseTooLarge(description);

        using var destination = new MemoryStream(Math.Min(maxBytes, contentLength is > 0 ? (int)contentLength : 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;
            if (destination.Length + read > maxBytes)
                throw ResponseTooLarge(description);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    private static IntegrationGatewayException ResponseTooLarge(string description) =>
        new(
            HttpStatusCode.BadGateway,
            $"{description} exceeded the configured size limit.",
            "casaos_response_too_large"
        );

    private static bool TryParseUpgradableApps(byte[] json, out HashSet<string> appIds)
    {
        appIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() > 1000)
                return false;

            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("store_app_id", out var idElement)
                    || idElement.ValueKind != JsonValueKind.String)
                    return false;
                var appId = idElement.GetString();
                if (appId is null || appId.Length is 0 or > 120)
                    return false;
                if (appId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
                    appIds.Add(appId);
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<SemaphoreSlim> AcquireAppLockAsync(
        string appId,
        CancellationToken cancellationToken
    )
    {
        var gate = _appLocks.GetOrAdd(appId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "A CasaOS operation is already in progress for this app.",
                "casaos_operation_in_progress"
            );
        return gate;
    }

    private static string SerializeAudit<T>(T value) => JsonSerializer.Serialize(value, AuditJsonOptions);

    private static string GetSafeErrorCode(Exception exception) => exception switch
    {
        IntegrationGatewayException gateway => gateway.Code,
        KeyNotFoundException => "backup_not_found",
        OperationCanceledException => "operation_cancelled",
        _ => "casaos_operation_failed",
    };

    private static Exception NormalizeOperationException(Exception exception) => exception switch
    {
        IntegrationGatewayException or KeyNotFoundException or ArgumentException or OperationCanceledException => exception,
        _ => new IntegrationGatewayException(
            HttpStatusCode.BadGateway,
            "The CasaOS operation could not be queued.",
            "casaos_operation_failed"
        ),
    };

    private static CasaOsQueuedOperationDto ToQueuedDto(
        IntegrationActionLog log,
        string backupId,
        string? safetyBackupId
    ) => new(
        log.Id,
        log.AppId!,
        log.Action,
        IntegrationActionStatus.Queued,
        "CasaOS accepted the request; completion has not been verified.",
        log.StartedAt,
        backupId,
        safetyBackupId
    );

    private static CasaOsActionStatusDto ToStatusDto(IntegrationActionLog log)
    {
        string? backupId = null;
        string? safetyBackupId = null;
        IReadOnlyList<string> previousImages = [];
        if (!string.IsNullOrWhiteSpace(log.ResultSummaryJson))
        {
            try
            {
                using var document = JsonDocument.Parse(
                    log.ResultSummaryJson,
                    new JsonDocumentOptions { MaxDepth = 8 }
                );
                var root = document.RootElement;
                backupId = ReadSafeBackupId(root, "backupId");
                safetyBackupId = ReadSafeBackupId(root, "safetyBackupId");
                if (root.TryGetProperty("previousImages", out var images)
                    && images.ValueKind == JsonValueKind.Array)
                    previousImages = images
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString())
                        .Where(item => item is not null && SafeImagePattern.IsMatch(item))
                        .Take(MaxAuditImages)
                        .Select(item => item!)
                        .ToList();
            }
            catch (JsonException)
            {
                // Old or malformed audit summaries remain visible without optional metadata.
            }
        }

        var message = log.Status switch
        {
            IntegrationActionStatus.Queued => "CasaOS accepted the request; completion has not been verified.",
            IntegrationActionStatus.Running => "Household began preparing the CasaOS request; its final outcome is unknown.",
            IntegrationActionStatus.Failed => "Household could not queue the CasaOS request.",
            _ => "Household recorded this CasaOS action.",
        };
        return new CasaOsActionStatusDto(
            log.Id,
            log.AppId!,
            log.Action,
            log.Status,
            message,
            log.StartedAt,
            log.FinishedAt,
            backupId,
            safetyBackupId,
            previousImages,
            log.ErrorMessage,
            log.Action == CasaOsUpdatePolicy.UpdateAction
                && backupId is not null
                && (log.Status is IntegrationActionStatus.Queued or IntegrationActionStatus.Succeeded)
        );
    }

    private static string? ReadSafeBackupId(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return null;
        var value = property.GetString();
        return value is not null && BackupIdPattern.IsMatch(value) ? value : null;
    }

    private sealed class Connection(Guid integrationId, string baseUrl, string rawToken, string? rawRefreshToken)
    {
        public Guid IntegrationId { get; } = integrationId;
        public string BaseUrl { get; } = baseUrl;
        public string RawToken { get; set; } = rawToken;
        public string? RawRefreshToken { get; set; } = rawRefreshToken;
    }

    private sealed record TokenPair(string AccessToken, string RefreshToken);

}

public sealed class CasaOsUpdateLocks
{
    internal ConcurrentDictionary<string, SemaphoreSlim> Locks { get; } = new(StringComparer.Ordinal);
}
