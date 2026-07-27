using System.Collections.Concurrent;
using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Infrastructure.Integrations.DoIt;

public sealed class DoItClient(HttpClient httpClient, IHouseholdProviderAccessService connectionAccess)
    : HouseholdProviderClientBase(httpClient, connectionAccess, "doit", "DoIt"), IDoItClient
{
    private static readonly ConcurrentDictionary<(Guid UserId, Guid OccurrenceId), OccurrenceContext> OccurrenceContexts = new();

    public async Task<DoItNowDto> GetNowAsync(Guid userId, string date, string timeZoneId, CancellationToken cancellationToken)
    {
        var query = BuildQuery(new Dictionary<string, string?> { ["date"] = date, ["timeZoneId"] = timeZoneId });
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
        var expiresAt = DateTime.UtcNow.AddHours(1);
        foreach (var task in tasks)
            OccurrenceContexts[(userId, task.OccurrenceId)] = new(task.OccurrenceDate, timeZoneId, expiresAt);
        foreach (var expired in OccurrenceContexts.Where(pair => pair.Value.ExpiresAt <= DateTime.UtcNow))
            OccurrenceContexts.TryRemove(expired.Key, out _);

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

    public async Task<IReadOnlyList<DoItCalendarEventDto>> GetCalendarEventsAsync(
        Guid userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["from"] = from?.ToUniversalTime().ToString("O"),
            ["to"] = to?.ToUniversalTime().ToString("O"),
        });
        return await GetRequiredAsync<List<DoItCalendarEventDto>>(
            userId,
            "calendar.read",
            $"/api/integrations/household/v1/calendar/events{query}",
            cancellationToken);
    }

    public async Task<DoItOccurrenceActionDto> CompleteOccurrenceAsync(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId,
        CancellationToken cancellationToken
    ) => await ApplyOccurrenceActionAsync(
        userId,
        occurrenceId,
        occurrenceDate,
        timeZoneId,
        "complete",
        "tasks.complete",
        "Done",
        cancellationToken);

    public async Task<DoItOccurrenceActionDto> UndoOccurrenceAsync(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId,
        CancellationToken cancellationToken
    ) => await ApplyOccurrenceActionAsync(
        userId,
        occurrenceId,
        occurrenceDate,
        timeZoneId,
        "undo",
        "tasks.undo",
        "Pending",
        cancellationToken);

    private async Task<DoItOccurrenceActionDto> ApplyOccurrenceActionAsync(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId,
        string action,
        string scope,
        string desiredStatus,
        CancellationToken cancellationToken
    )
    {
        var context = ResolveOccurrenceContext(userId, occurrenceId, occurrenceDate, timeZoneId);
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["date"] = context.Date,
            ["timeZoneId"] = context.TimeZoneId,
        });
        try
        {
            return await PostRequiredAsync<DoItOccurrenceActionDto>(
                userId,
                scope,
                $"/api/integrations/household/v1/occurrences/{occurrenceId}/{action}{query}",
                cancellationToken
            );
        }
        catch (Application.Exceptions.IntegrationGatewayException exception) when (exception.Code == "ambiguous_timeout")
        {
            var canonical = (await GetNowAsync(userId, context.Date, context.TimeZoneId, cancellationToken)).Tasks
                .SingleOrDefault(task => task.OccurrenceId == occurrenceId);
            if (canonical is not null && string.Equals(canonical.OccurrenceStatus, desiredStatus, StringComparison.OrdinalIgnoreCase))
                return new DoItOccurrenceActionDto(canonical.OccurrenceId, canonical.Id, canonical.OccurrenceDate, canonical.OccurrenceStatus);
            throw new Application.Exceptions.IntegrationGatewayException(
                System.Net.HttpStatusCode.Conflict,
                "DoIt did not confirm the requested action.",
                "mutation_unconfirmed",
                reconcilable: true
            );
        }
    }

    private static OccurrenceContext ResolveOccurrenceContext(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(occurrenceDate))
            return new OccurrenceContext(occurrenceDate, timeZoneId, DateTime.UtcNow.AddHours(1));
        if (OccurrenceContexts.TryGetValue((userId, occurrenceId), out var context) && context.ExpiresAt > DateTime.UtcNow)
            return context;

        throw new Application.Exceptions.IntegrationGatewayException(
            System.Net.HttpStatusCode.Conflict,
            "Reload DoIt tasks before applying this action.",
            "occurrence_context_missing",
            reconcilable: true
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
            task.RecommendedTime,
            task.AssignmentMode,
            task.AssigneeIds,
            task.AssigneeNames,
            task.TimeZoneId,
            task.RecurrenceType,
            task.CompletedAt,
            task.CompletedByUserId
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
        public string AssignmentMode { get; set; } = string.Empty;
        public List<Guid> AssigneeIds { get; set; } = [];
        public List<string> AssigneeNames { get; set; } = [];
        public string TimeZoneId { get; set; } = "UTC";
        public string RecurrenceType { get; set; } = "Manual";
        public DateTime? CompletedAt { get; set; }
        public Guid? CompletedByUserId { get; set; }
    }

    private sealed record OccurrenceContext(string Date, string TimeZoneId, DateTime ExpiresAt);
}
