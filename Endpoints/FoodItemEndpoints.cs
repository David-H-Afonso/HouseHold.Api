using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class FoodItemEndpoints
{
    public static void MapFoodItemEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/food-items").WithTags("Food").RequireAuthorization();

        group
            .MapGet(
                "/",
                async (IFoodItemService service, string? search) => Results.Ok(await service.GetAllAsync(search))
            )
            .WithName("GetFoodItems");

        group
            .MapGet(
                "/{id:guid}",
                async (Guid id, IFoodItemService service) =>
                {
                    var item = await service.GetByIdAsync(id);
                    return item == null ? Results.NotFound() : Results.Ok(item);
                }
            )
            .WithName("GetFoodItemById");

        group
            .MapPost(
                "/",
                async (CreateFoodItemRequest request, IFoodItemService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var item = await service.CreateAsync(request, userId.Value);
                    return Results.Created($"/food-items/{item.Id}", item);
                }
            )
            .WithName("CreateFoodItem");

        group
            .MapPut(
                "/{id:guid}",
                async (Guid id, UpdateFoodItemRequest request, IFoodItemService service) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var item = await service.UpdateAsync(id, request);
                    return item == null ? Results.NotFound() : Results.Ok(item);
                }
            )
            .WithName("UpdateFoodItem");

        group
            .MapDelete(
                "/{id:guid}",
                async (Guid id, IFoodItemService service) =>
                {
                    var deleted = await service.DeleteAsync(id);
                    return deleted ? Results.NoContent() : Results.NotFound();
                }
            )
            .WithName("DeleteFoodItem");
    }
}
