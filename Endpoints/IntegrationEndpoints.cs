using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/integrations").WithTags("Integrations").RequireAuthorization();

        group
            .MapGet("/", async (IIntegrationService service, CancellationToken ct) =>
                Results.Ok(await service.GetAllAsync(ct))
            )
            .WithName("GetIntegrations")
            .WithSummary("List configured integrations without secret values");

        group
            .MapPost(
                "/",
                async (UpsertIntegrationRequest request, IIntegrationService service, HttpContext ctx, CancellationToken ct) =>
                {
                    if (!ctx.IsAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var created = await service.CreateAsync(request, ct);
                    return Results.Created($"/integrations/{created.Id}", created);
                }
            )
            .WithName("CreateIntegration")
            .WithSummary("Admin: create an integration. Secret fields are write-only.");

        group
            .MapPut(
                "/{id:guid}",
                async (
                    Guid id,
                    UpsertIntegrationRequest request,
                    IIntegrationService service,
                    HttpContext ctx,
                    CancellationToken ct
                ) =>
                {
                    if (!ctx.IsAdmin())
                        return Results.Forbid();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var updated = await service.UpdateAsync(id, request, ct);
                    return updated is null ? Results.NotFound() : Results.Ok(updated);
                }
            )
            .WithName("UpdateIntegration")
            .WithSummary("Admin: update an integration. Secret fields are write-only.");

        group
            .MapDelete(
                "/{id:guid}",
                async (Guid id, IIntegrationService service, HttpContext ctx, CancellationToken ct) =>
                {
                    if (!ctx.IsAdmin())
                        return Results.Forbid();

                    return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
                }
            )
            .WithName("DeleteIntegration")
            .WithSummary("Admin: delete an integration");

        group
            .MapGet(
                "/{id:guid}/health",
                async (Guid id, IIntegrationHealthService service, CancellationToken ct) =>
                {
                    try
                    {
                        return Results.Ok(await service.GetHealthAsync(id, ct));
                    }
                    catch (KeyNotFoundException)
                    {
                        return Results.NotFound();
                    }
                }
            )
            .WithName("GetIntegrationHealth")
            .WithSummary("Get health for one integration");

        group
            .MapGet("/health", async (IIntegrationHealthService service, CancellationToken ct) =>
                Results.Ok(await service.GetAllHealthAsync(ct))
            )
            .WithName("GetAllIntegrationHealth")
            .WithSummary("Get health for all configured integrations");
    }
}
