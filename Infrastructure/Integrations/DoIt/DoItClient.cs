using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Infrastructure.Integrations.DoIt;

public sealed class DoItClient(HttpClient httpClient, IHouseholdProviderAccessService connectionAccess)
    : HouseholdProviderClientBase(httpClient, connectionAccess, "doit", "DoIt"), IDoItClient
{
    public async Task<DoItNowDto> GetNowAsync(Guid userId, string? date, CancellationToken cancellationToken)
    {
        var query = BuildQuery(new Dictionary<string, string?> { ["date"] = date });
        var source = await GetRequiredAsync<SourceNow>(
            userId,
            "tasks.read",
            $"/api/integrations/household/v1/now{query}",
            cancellationToken
        );

        var tasks = new List<DoItNowTaskDto>();
        foreach (var zone in source.Zones)
        {
            tasks.AddRange(zone.Overdue.Select(task => ToDto(task, "Overdue")));
            tasks.AddRange(zone.Available.Select(task => ToDto(task, "Available")));
            tasks.AddRange(zone.Unavailable.Select(task => ToDto(task, "Unavailable")));
            tasks.AddRange(zone.Completed.Select(task => ToDto(task, "Completed")));
        }
        tasks.AddRange(source.Upcoming.Select(task => ToDto(task, "Upcoming")));

        return new DoItNowDto(
            source.Date,
            source.Scope,
            new DoItProgressDto(
                source.Progress.Total,
                source.Progress.Done,
                source.Progress.Missed,
                source.Progress.NotApplicable,
                source.Progress.Pending
            ),
            tasks
        );
    }

    private static DoItNowTaskDto ToDto(SourceTask task, string state) =>
        new(
            task.OccurrenceId,
            task.Id,
            task.Title,
            task.ZoneName,
            task.Scope,
            state,
            task.OccurrenceStatus,
            task.OccurrenceDate,
            task.AvailableFromTime,
            task.AvailableUntilTime,
            task.RecommendedTime
        );

    private sealed class SourceNow
    {
        public string Date { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public SourceProgress Progress { get; set; } = new();
        public List<SourceZone> Zones { get; set; } = [];
        public List<SourceTask> Upcoming { get; set; } = [];
    }

    private sealed class SourceProgress
    {
        public int Total { get; set; }
        public int Done { get; set; }
        public int Missed { get; set; }
        public int NotApplicable { get; set; }
        public int Pending { get; set; }
    }

    private sealed class SourceZone
    {
        public List<SourceTask> Overdue { get; set; } = [];
        public List<SourceTask> Available { get; set; } = [];
        public List<SourceTask> Unavailable { get; set; } = [];
        public List<SourceTask> Completed { get; set; } = [];
    }

    private sealed class SourceTask
    {
        public Guid OccurrenceId { get; set; }
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ZoneName { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string OccurrenceStatus { get; set; } = string.Empty;
        public string OccurrenceDate { get; set; } = string.Empty;
        public string? AvailableFromTime { get; set; }
        public string? AvailableUntilTime { get; set; }
        public string? RecommendedTime { get; set; }
    }
}
