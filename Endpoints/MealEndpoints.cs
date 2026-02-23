using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class MealEndpoints
{
    public static void MapMealEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/meal-entries")
            .WithTags("Food")
            .RequireAuthorization();

        group.MapGet("/", async (
            IMealService service,
            HttpContext ctx,
            DateTime? from,
            DateTime? to) =>
        {
            var userId = ctx.GetUserId();
            if (userId == null) return Results.Unauthorized();
            return Results.Ok(await service.GetAllAsync(userId.Value, from, to));
        })
        .WithName("GetMealEntries");

        group.MapGet("/{id:guid}", async (Guid id, IMealService service) =>
        {
            var entry = await service.GetByIdAsync(id);
            return entry == null ? Results.NotFound() : Results.Ok(entry);
        })
        .WithName("GetMealEntryById");

        group.MapPost("/", async (CreateMealEntryRequest request, IMealService service, HttpContext ctx) =>
        {
            var userId = ctx.GetUserId();
            if (userId == null) return Results.Unauthorized();

            var entry = await service.CreateAsync(request, userId.Value);
            return Results.Created($"/meal-entries/{entry.Id}", entry);
        })
        .WithName("CreateMealEntry");

        group.MapPut("/{id:guid}", async (Guid id, UpdateMealEntryRequest request, IMealService service, HttpContext ctx) =>
        {
            var userId = ctx.GetUserId();
            if (userId == null) return Results.Unauthorized();

            var entry = await service.UpdateAsync(id, request, userId.Value);
            return entry == null ? Results.NotFound() : Results.Ok(entry);
        })
        .WithName("UpdateMealEntry");

        group.MapDelete("/{id:guid}", async (Guid id, IMealService service, HttpContext ctx) =>
        {
            var userId = ctx.GetUserId();
            if (userId == null) return Results.Unauthorized();

            var deleted = await service.DeleteAsync(id, userId.Value);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMealEntry");
    }
}
