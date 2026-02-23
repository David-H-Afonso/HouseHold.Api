using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        // ── TaskTemplate CRUD ─────────────────────────────────────────────────

        var templates = app.MapGroup("/task-templates").WithTags("Home").RequireAuthorization();

        templates
            .MapGet("/", async (ITaskService service) => Results.Ok(await service.GetAllTemplatesAsync()))
            .WithName("GetTaskTemplates");

        templates
            .MapGet(
                "/{id:guid}",
                async (Guid id, ITaskService service) =>
                {
                    var tt = await service.GetTemplateByIdAsync(id);
                    return tt == null ? Results.NotFound() : Results.Ok(tt);
                }
            )
            .WithName("GetTaskTemplateById");

        templates
            .MapPost(
                "/",
                async (CreateTaskTemplateRequest request, ITaskService service) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Title))
                        return Results.BadRequest(new { message = "Title is required." });

                    var tt = await service.CreateTemplateAsync(request);
                    return Results.Created($"/task-templates/{tt.Id}", tt);
                }
            )
            .WithName("CreateTaskTemplate");

        templates
            .MapPut(
                "/{id:guid}",
                async (Guid id, UpdateTaskTemplateRequest request, ITaskService service) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Title))
                        return Results.BadRequest(new { message = "Title is required." });

                    var tt = await service.UpdateTemplateAsync(id, request);
                    return tt == null ? Results.NotFound() : Results.Ok(tt);
                }
            )
            .WithName("UpdateTaskTemplate");

        templates
            .MapDelete(
                "/{id:guid}",
                async (Guid id, ITaskService service) =>
                {
                    var deleted = await service.DeleteTemplateAsync(id);
                    return deleted ? Results.NoContent() : Results.NotFound();
                }
            )
            .WithName("DeleteTaskTemplate");

        // ── Task instances ────────────────────────────────────────────────────

        var instances = app.MapGroup("/tasks").WithTags("Home").RequireAuthorization();

        instances
            .MapGet("/today", async (ITaskService service) => Results.Ok(await service.GetTodayTasksAsync()))
            .WithName("GetTodayTasks")
            .WithSummary(
                "Returns today's task instances (idempotently created) grouped by time slot, plus overdue ones."
            );

        instances
            .MapPost(
                "/instances/{id:guid}/complete",
                async (Guid id, CompleteTaskInstanceRequest request, ITaskService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    var instance = await service.CompleteTaskInstanceAsync(id, userId.Value, request.Notes);
                    return instance == null ? Results.NotFound() : Results.Ok(instance);
                }
            )
            .WithName("CompleteTaskInstance");
    }
}
