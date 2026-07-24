using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.GitHub;

public sealed class GitHubActionsRuntimeCache
{
    internal ConcurrentDictionary<string, GitHubActionsMonitor.CacheEntry> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal DateTime? LastSuccessfulPoll { get; set; }
    public DateTime BackoffUntil { get; internal set; }
    internal bool PollInProgress { get; set; }
    internal object SyncRoot { get; } = new();
}

public sealed class GitHubActionsMonitor : IGitHubActionsMonitor
{
    public static readonly IReadOnlyList<string> Repositories =
    [
        "David-H-Afonso/BeastVault.Front",
        "David-H-Afonso/BeastVault.Api",
        "David-H-Afonso/GamesDatabase.Front",
        "David-H-Afonso/GamesDatabase.Api",
        "David-H-Afonso/Jellywatch.Front",
        "David-H-Afonso/Jellywatch.Api",
        "David-H-Afonso/DoIt.Front",
        "David-H-Afonso/DoIt.Api",
        "David-H-Afonso/WarcraftArchive.Front",
        "David-H-Afonso/WarcraftArchive.Api",
        "David-H-Afonso/HouseHold.Front",
        "David-H-Afonso/HouseHold.Api",
    ];

    private const int MaxResponseBytes = 256 * 1024;
    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly ExternalIntegrationSettings _settings;
    private readonly GitHubActionsRuntimeCache _cache;

    public GitHubActionsMonitor(
        AppDbContext db,
        HttpClient httpClient,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ExternalIntegrationSettings> settings,
        GitHubActionsRuntimeCache cache
    )
    {
        _db = db;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Household", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _protector = dataProtectionProvider.CreateProtector("Household.GitHub.ReadOnlyPat.v1");
        _settings = settings.Value;
        _cache = cache;
    }

    public async Task<GitHubActionsConfigDto> GetConfigAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        var hasToken = integration?.Secrets.Any(item => item.SecretKey == "read-only-pat") == true;
        return new GitHubActionsConfigDto(integration?.Enabled == true && hasToken, hasToken);
    }

    public async Task<GitHubActionsConfigDto> UpdateConfigAsync(
        UpdateGitHubActionsConfigRequest request,
        CancellationToken cancellationToken
    )
    {
        var integration = await LoadIntegrationAsync(cancellationToken) ?? new Integration
        {
            Type = IntegrationType.GitHubActions,
            Name = "GitHub Actions",
        };
        if (_db.Entry(integration).State == EntityState.Detached) _db.Integrations.Add(integration);
        integration.Enabled = true;
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            if (request.Token.Length is < 20 or > 1000) throw new ArgumentException("GitHub token is invalid.");
            var secret = integration.Secrets.SingleOrDefault(item => item.SecretKey == "read-only-pat");
            if (secret is null)
            {
                secret = new IntegrationSecret { SecretKey = "read-only-pat" };
                integration.Secrets.Add(secret);
            }
            secret.ProtectedValue = _protector.Protect(request.Token.Trim());
        }
        await _db.SaveChangesAsync(cancellationToken);
        return await GetConfigAsync(cancellationToken);
    }

    public async Task<GitHubActionsMonitorDto> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var hiddenJson = await _db.UserPreferences.AsNoTracking().Where(item => item.UserId == userId)
            .Select(item => item.HiddenGitHubReposJson).SingleOrDefaultAsync(cancellationToken);
        HashSet<string> hidden;
        try { hidden = JsonSerializer.Deserialize<HashSet<string>>(hiddenJson ?? "[]") ?? []; }
        catch (JsonException) { hidden = []; }
        var rows = Repositories.Where(repository => !hidden.Contains(repository, StringComparer.OrdinalIgnoreCase))
            .Select(repository => _cache.Repositories.TryGetValue(repository, out var entry)
                ? entry.Run with { Repository = repository }
                : new GitHubWorkflowRunDto(repository, null, null, null, null, null, null, null, null, null, null, null, null, true, "not_polled"))
            .ToList();
        return new GitHubActionsMonitorDto(DateTime.UtcNow, _cache.LastSuccessfulPoll, rows.Any(row => row.Degraded), rows);
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        lock (_cache.SyncRoot)
        {
            if (_cache.PollInProgress || _cache.BackoffUntil > DateTime.UtcNow) return;
            _cache.PollInProgress = true;
        }
        try
        {
            var token = await GetTokenAsync(cancellationToken);
            if (token is null) return;
            using var gate = new SemaphoreSlim(Math.Clamp(_settings.GitHubConcurrency, 1, 6));
            await Task.WhenAll(Repositories.Select(async repository =>
            {
                await gate.WaitAsync(cancellationToken);
                try { await PollRepositoryAsync(repository, token, cancellationToken); }
                finally { gate.Release(); }
            }));
            if (_cache.Repositories.Values.Any(entry => !entry.Run.Degraded)) _cache.LastSuccessfulPoll = DateTime.UtcNow;
        }
        finally
        {
            lock (_cache.SyncRoot) _cache.PollInProgress = false;
        }
    }

    private async Task PollRepositoryAsync(string repository, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/actions/runs?per_page=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_cache.Repositories.TryGetValue(repository, out var existing) && existing.ETag is not null)
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(existing.ETag));
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotModified && existing is not null)
            {
                _cache.Repositories[repository] = existing with { Run = existing.Run with { LastSuccessfulPoll = DateTime.UtcNow, Degraded = false, ErrorCode = null } };
                return;
            }
            if ((int)response.StatusCode is 403 or 429)
            {
                var reset = response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                    && long.TryParse(values.FirstOrDefault(), out var epoch)
                    ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime
                    : DateTime.UtcNow.AddMinutes(2);
                _cache.BackoffUntil = reset > DateTime.UtcNow ? reset : DateTime.UtcNow.AddMinutes(2);
                SetError(repository, "rate_limited");
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                SetError(repository, "github_unavailable");
                return;
            }
            await response.Content.LoadIntoBufferAsync(MaxResponseBytes, cancellationToken);
            var source = await JsonSerializer.DeserializeAsync<SourceRuns>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 12 },
                cancellationToken
            );
            var run = source?.WorkflowRuns.FirstOrDefault();
            var now = DateTime.UtcNow;
            var dto = new GitHubWorkflowRunDto(
                repository,
                run?.Id,
                NormalizeProviderText(run?.Name, 200),
                NormalizeProviderText(run?.Status, 40),
                NormalizeProviderText(run?.Conclusion, 40),
                NormalizeProviderText(run?.HeadBranch, 255),
                run?.HeadSha is { Length: > 7 } sha ? sha[..7] : run?.HeadSha,
                NormalizeProviderText(run?.Actor?.Login, 100),
                run?.RunStartedAt,
                run?.UpdatedAt,
                CalculateDurationSeconds(run?.RunStartedAt, run?.UpdatedAt),
                IsSafeGitHubUrl(run?.HtmlUrl) ? run?.HtmlUrl : null,
                now,
                false,
                null
            );
            _cache.Repositories[repository] = new CacheEntry(response.Headers.ETag?.Tag, dto);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            SetError(repository, "github_unavailable");
        }
    }

    private void SetError(string repository, string code)
    {
        if (_cache.Repositories.TryGetValue(repository, out var existing))
            _cache.Repositories[repository] = existing with { Run = existing.Run with { Degraded = true, ErrorCode = code } };
        else
            _cache.Repositories[repository] = new CacheEntry(null, new GitHubWorkflowRunDto(
                repository, null, null, null, null, null, null, null, null, null, null, null, null, true, code));
    }

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        var secret = integration?.Secrets.SingleOrDefault(item => item.SecretKey == "read-only-pat");
        if (integration?.Enabled != true || secret is null) return null;
        try { return _protector.Unprotect(secret.ProtectedValue); }
        catch (CryptographicException) { return null; }
    }

    private Task<Integration?> LoadIntegrationAsync(CancellationToken cancellationToken) =>
        _db.Integrations.Include(item => item.Secrets)
            .SingleOrDefaultAsync(item => item.Type == IntegrationType.GitHubActions && item.Name == "GitHub Actions", cancellationToken);

    private static bool IsSafeGitHubUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static string? NormalizeProviderText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static long? CalculateDurationSeconds(DateTime? startedAt, DateTime? completedAt) =>
        startedAt is not null && completedAt is not null && completedAt >= startedAt
            ? (long)(completedAt.Value - startedAt.Value).TotalSeconds
            : null;

    internal sealed record CacheEntry(string? ETag, GitHubWorkflowRunDto Run);
    private sealed class SourceRuns
    {
        [JsonPropertyName("workflow_runs")]
        public List<SourceRun> WorkflowRuns { get; set; } = [];
    }
    private sealed class SourceRun
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? Conclusion { get; set; }
        [JsonPropertyName("head_branch")]
        public string? HeadBranch { get; set; }
        [JsonPropertyName("head_sha")]
        public string? HeadSha { get; set; }
        public SourceActor? Actor { get; set; }
        [JsonPropertyName("run_started_at")]
        public DateTime? RunStartedAt { get; set; }
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
    private sealed class SourceActor { public string? Login { get; set; } }
}

public sealed class GitHubActionsPollingService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExternalIntegrationSettings> settings,
    ILogger<GitHubActionsPollingService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(settings.Value.GitHubPollSeconds, 45, 90)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IGitHubActionsMonitor>().PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "GitHub Actions poll failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
