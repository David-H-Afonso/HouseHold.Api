using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Infrastructure.Integrations.WarcraftArchive;

public sealed class WarcraftArchiveClient(HttpClient httpClient, IHouseholdProviderAccessService connectionAccess)
    : HouseholdProviderClientBase(httpClient, connectionAccess, "warcraft-archive", "Warcraft Archive"),
        IWarcraftArchiveClient
{
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
                StatusLabel(item.Status),
                item.LastCompletedAt,
                item.UpdatedAt
            )).ToList()
        );
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
        0 => "NotStarted",
        1 => "Pending",
        2 => "InProgress",
        3 => "LastDay",
        4 => "LastWeek",
        5 => "Finished",
        _ => "Unknown",
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
        public int Status { get; set; }
        public DateTime? LastCompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
