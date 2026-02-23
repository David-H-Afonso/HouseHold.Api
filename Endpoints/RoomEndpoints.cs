using Household.Api.DTOs;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/rooms").WithTags("Home").RequireAuthorization();

        group.MapGet("/", async (IRoomService service) => Results.Ok(await service.GetAllAsync())).WithName("GetRooms");

        group
            .MapGet(
                "/{id:guid}",
                async (Guid id, IRoomService service) =>
                {
                    var room = await service.GetByIdAsync(id);
                    return room == null ? Results.NotFound() : Results.Ok(room);
                }
            )
            .WithName("GetRoomById");

        group
            .MapPost(
                "/",
                async (CreateRoomRequest request, IRoomService service) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var room = await service.CreateAsync(request);
                    return Results.Created($"/rooms/{room.Id}", room);
                }
            )
            .WithName("CreateRoom");

        group
            .MapPut(
                "/{id:guid}",
                async (Guid id, UpdateRoomRequest request, IRoomService service) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Results.BadRequest(new { message = "Name is required." });

                    var room = await service.UpdateAsync(id, request);
                    return room == null ? Results.NotFound() : Results.Ok(room);
                }
            )
            .WithName("UpdateRoom");

        group
            .MapDelete(
                "/{id:guid}",
                async (Guid id, IRoomService service) =>
                {
                    var deleted = await service.DeleteAsync(id);
                    return deleted ? Results.NoContent() : Results.NotFound();
                }
            )
            .WithName("DeleteRoom");
    }
}
