using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public class DashboardAggregationService : IDashboardAggregationService
{
    private readonly AppDbContext _db;
    private readonly IIntegrationHealthService _healthService;

    public DashboardAggregationService(AppDbContext db, IIntegrationHealthService healthService)
    {
        _db = db;
        _healthService = healthService;
    }

    public async Task<DashboardResponse> GetDashboardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var widgets = await _db
            .DashboardWidgets.Where(w => w.UserId == userId && w.Enabled)
            .OrderBy(w => w.Position)
            .Select(w => new DashboardWidgetDto(w.Id, w.WidgetType, w.IntegrationId, w.Position, w.Enabled, w.SettingsJson))
            .ToListAsync(cancellationToken);

        var health = await _healthService.GetAllHealthAsync(cancellationToken);
        return new DashboardResponse(DateTime.UtcNow, health, widgets);
    }
}
