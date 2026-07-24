using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.WarcraftArchive;

public sealed class WarcraftArchiveClient : HouseholdProviderClientBase, IWarcraftArchiveClient
{
    private readonly ExternalIntegrationSettings _settings;

    public WarcraftArchiveClient(
        HttpClient httpClient,
        IHouseholdProviderAccessService connectionAccess,
        IOptions<ExternalIntegrationSettings> settings
    ) : base(httpClient, connectionAccess, "warcraft-archive", "Warcraft Archive") => _settings = settings.Value;
    public Task<WarcraftQuickStatusDto> GetQuickStatusAsync(Guid userId, CancellationToken cancellationToken) =>
        GetRequiredAsync<WarcraftQuickStatusDto>(userId, "dashboard.read", "/dashboard/quick-status", cancellationToken);

    public async Task<WarcraftWeeklyDto> GetWeeklyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var source = await GetRequiredAsync<SourceWeekly>(userId, "dashboard.read", "/dashboard/weekly", cancellationToken);
        var remaining = Math.Max(0, source.Total - source.Finished);
        return new WarcraftWeeklyDto(
            DateTime.UtcNow,
            new WarcraftWeeklySummaryDto(
                source.Total,
                source.NotStarted,
                source.Pending,
                source.InProgress,
                source.LastDay,
                source.LastWeek,
                source.Finished,
                remaining,
                source.Total == 0 ? 0 : (int)Math.Round(source.Finished * 100d / source.Total)
            ),
            source.Items.Select(item => new WarcraftWeeklyItemDto(
                item.Id,
                item.CharacterName,
                item.CharacterClass,
                item.ContentName,
                item.Expansion,
                DifficultyLabel(item.Difficulty),
                FrequencyLabel(item.Frequency),
                StatusLabel(item.Status),
                item.LastCompletedAt,
                item.UpdatedAt
            )).ToList()
        );
    }

    public async Task<WarcraftWeeklyItemDto> UpdateTrackingStatusAsync(
        Guid userId,
        Guid id,
        string status,
        CancellationToken cancellationToken
    )
    {
        var normalized = NormalizeStatus(status);
        var path = BuildConfiguredPath(_settings.WarcraftStatusPathTemplate, id.ToString());
        var item = await PatchRequiredAsync<SourceWeeklyItem>(userId, "tracking.status.write", path, new { status = StatusValue(normalized) }, cancellationToken);
        return ToItem(item);
    }

    private static string DifficultyLabel(int value)
    {
        var labels = new List<string>();
        if ((value & 1) != 0) labels.Add("LFR");
        if ((value & 2) != 0) labels.Add("Normal");
        if ((value & 4) != 0) labels.Add("Heroic");
        if ((value & 8) != 0) labels.Add("Mythic");
        return labels.Count == 0 ? "Unspecified" : string.Join(" / ", labels);
    }

    private static string StatusLabel(int value) => value switch
    {
         0 => "Not started",
         1 => "Pending",
         2 => "In progress",
         3 => "Completed last day",
         4 => "Completed last week",
         5 => "Finished",
         _ => "Unknown status",
    };

    private static string NormalizeStatus(string value) => value.Trim().ToLowerInvariant() switch
    {
        "notstarted" or "not started" => "NotStarted",
        "pending" => "Pending",
        "inprogress" or "in progress" => "InProgress",
        "lastday" or "last day" => "LastDay",
        "lastweek" or "last week" => "LastWeek",
        "finished" => "Finished",
        _ => throw new ArgumentException("Unsupported Warcraft tracking status."),
    };

    private static int StatusValue(string status) => status switch
    {
        "NotStarted" => 0,
        "Pending" => 1,
        "InProgress" => 2,
        "LastDay" => 3,
        "LastWeek" => 4,
        "Finished" => 5,
        _ => throw new ArgumentException("Unsupported Warcraft tracking status."),
    };

    private static string BuildConfiguredPath(string template, string id)
    {
        var path = template.Replace("{id}", id, StringComparison.Ordinal);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
            throw new ArgumentException("Warcraft status path template must be a relative provider path.");
        return path;
    }

    private static WarcraftWeeklyItemDto ToItem(SourceWeeklyItem item) => new(
        item.Id,
        item.CharacterName,
        item.CharacterClass,
        item.ContentName,
        item.Expansion,
        DifficultyLabel(item.Difficulty),
        FrequencyLabel(item.Frequency),
        StatusLabel(item.Status),
        item.LastCompletedAt,
        item.UpdatedAt
    );

    private static string FrequencyLabel(int value) => value switch
    {
        0 => "Hourly",
        1 => "Daily",
        2 => "Weekly",
        3 => "Monthly",
        _ => "Unspecified",
    };

    private sealed class SourceWeekly
    {
        public int Total { get; set; }
        public int NotStarted { get; set; }
        public int Pending { get; set; }
        public int InProgress { get; set; }
        public int LastDay { get; set; }
        public int LastWeek { get; set; }
        public int Finished { get; set; }
        public List<SourceWeeklyItem> Items { get; set; } = [];
    }

    private sealed class SourceWeeklyItem
    {
        public Guid Id { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public string? CharacterClass { get; set; }
        public string ContentName { get; set; } = string.Empty;
        public string Expansion { get; set; } = string.Empty;
        public int Difficulty { get; set; }
        public int Frequency { get; set; } = 2;
        public int Status { get; set; }
        public DateTime? LastCompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
