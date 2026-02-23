using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin")
            .WithTags("Admin")
            .RequireAuthorization();

        // Only admin can create users
        group.MapPost("/users", async (CreateUserRequest request, IAuthService authService, HttpContext ctx) =>
        {
            if (!ctx.IsAdmin())
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { message = "Email and password are required." });

            var user = await authService.CreateUserAsync(request.Email, request.UserName, request.Password, request.IsAdmin);

            if (user == null)
                return Results.Conflict(new { message = "A user with that email already exists." });

            return Results.Created($"/admin/users/{user.Id}", new UserDto(
                user.Id, user.Email, user.UserName, user.IsAdmin, user.IsActive,
                user.CreatedAt, user.UpdatedAt));
        })
        .WithName("CreateUser")
        .WithSummary("Admin: create a new user");
    }
}
