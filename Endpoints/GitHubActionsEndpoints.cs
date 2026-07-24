using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class GitHubActionsEndpoints
{
    public static void MapGitHubActionsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/github-actions").WithTags("GitHub Actions").RequireAuthorization();
        group.MapGet("/", async (HttpContext context, IGitHubActionsMonitor monitor, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await monitor.GetForUserAsync(userId.Value, ct));
        });
        group.MapGet("/config", async (HttpContext context, IGitHubActionsMonitor monitor, CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await monitor.GetConfigAsync(ct)) : Results.Forbid());
        group.MapPut("/config", async (
            UpdateGitHubActionsConfigRequest request,
            HttpContext context,
            IGitHubActionsMonitor monitor,
            CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await monitor.UpdateConfigAsync(request, ct)) : Results.Forbid()
        ).RequireRateLimiting("admin");
    }
}
