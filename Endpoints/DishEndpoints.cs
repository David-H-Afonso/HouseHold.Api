using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class DishEndpoints
{
    public static void MapDishEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dish-templates").WithTags("Food").RequireAuthorization();

        group
            .MapGet(
                "/",
                async (IDishService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();
                    return Results.Ok(await service.GetAllAsync(userId.Value));
                }
            )
            .WithName("GetDishTemplates");

        group
            .MapGet(
                "/{id:guid}",
                async (Guid id, IDishService service) =>
                {
                    var dish = await service.GetByIdAsync(id);
                    return dish == null ? Results.NotFound() : Results.Ok(dish);
                }
            )
            .WithName("GetDishTemplateById");

        group
            .MapPost(
                "/",
                async (CreateDishTemplateRequest request, IDishService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var dish = await service.CreateAsync(request, userId.Value);
                    return Results.Created($"/dish-templates/{dish.Id}", dish);
                }
            )
            .WithName("CreateDishTemplate");

        group
            .MapPut(
                "/{id:guid}",
                async (Guid id, UpdateDishTemplateRequest request, IDishService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var dish = await service.UpdateAsync(id, request, userId.Value);
                    return dish == null ? Results.NotFound() : Results.Ok(dish);
                }
            )
            .WithName("UpdateDishTemplate");

        group
            .MapDelete(
                "/{id:guid}",
                async (Guid id, IDishService service, HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    var deleted = await service.DeleteAsync(id, userId.Value);
                    return deleted ? Results.NoContent() : Results.NotFound();
                }
            )
            .WithName("DeleteDishTemplate");
    }
}
