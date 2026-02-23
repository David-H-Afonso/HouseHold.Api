using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class IssueEndpoints
{
    public static void MapIssueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/issues")
            .WithTags("Home")
            .RequireAuthorization();

        group.MapGet("/", async (IIssueService service) =>
            Results.Ok(await service.GetAllAsync()))
            .WithName("GetIssues");

        group.MapGet("/{id:guid}", async (Guid id, IIssueService service) =>
        {
            var issue = await service.GetByIdAsync(id);
            return issue == null ? Results.NotFound() : Results.Ok(issue);
        })
        .WithName("GetIssueById");

        group.MapPost("/", async (CreateHomeIssueRequest request, IIssueService service, HttpContext ctx) =>
        {
            var userId = ctx.GetUserId();
            if (userId == null) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Title is required." });

            var issue = await service.CreateAsync(request, userId.Value);
            return Results.Created($"/issues/{issue.Id}", issue);
        })
        .WithName("CreateIssue");

        group.MapPut("/{id:guid}", async (Guid id, UpdateHomeIssueRequest request, IIssueService service) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { message = "Title is required." });

            var issue = await service.UpdateAsync(id, request);
            return issue == null ? Results.NotFound() : Results.Ok(issue);
        })
        .WithName("UpdateIssue");

        group.MapDelete("/{id:guid}", async (Guid id, IIssueService service) =>
        {
            var deleted = await service.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteIssue");
    }
}
