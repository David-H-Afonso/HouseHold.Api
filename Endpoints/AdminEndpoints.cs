using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization().RequireRateLimiting("admin");

        group.MapGet("/users", async (HttpContext context, IUserAdministrationService service, CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetUsersAsync(ct)) : Results.Forbid());

        group.MapPost("/users", async (
            AdminCreateUserRequest request,
            HttpContext context,
            IUserAdministrationService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorId) return Results.Forbid();
            var result = await service.CreateUserAsync(actorId, request, ct);
            return result.Error switch
            {
                null => Results.Created($"/admin/users/{result.User!.Id}", new { user = result.User, temporaryPassword = result.TemporaryPassword }),
                "email_exists" => Results.Conflict(new { code = result.Error }),
                _ => Results.BadRequest(new { code = result.Error }),
            };
        });

        group.MapPatch("/users/{id:guid}", async (
            Guid id,
            AdminUpdateUserRequest request,
            HttpContext context,
            IUserAdministrationService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorId) return Results.Forbid();
            var result = await service.UpdateUserAsync(actorId, id, request, ct);
            return result.Error switch
            {
                null => Results.Ok(result.User),
                "not_found" => Results.NotFound(),
                "email_exists" or "last_admin" => Results.Conflict(new { code = result.Error }),
                _ => Results.BadRequest(new { code = result.Error }),
            };
        });

        group.MapPost("/users/{id:guid}/reset-password", async (
            Guid id,
            HttpContext context,
            IUserAdministrationService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorId) return Results.Forbid();
            var result = await service.ResetPasswordAsync(actorId, id, ct);
            return result.Error == "not_found"
                ? Results.NotFound()
                : Results.Ok(new TemporaryPasswordDto(id, result.TemporaryPassword!));
        });

        group.MapPost("/invitations", async (
            CreateInvitationRequest request,
            HttpContext context,
            IUserAdministrationService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorId) return Results.Forbid();
            var result = await service.CreateInvitationAsync(actorId, request, ct);
            return result.Error is null ? Results.Ok(result.Invitation) : Results.BadRequest(new { code = result.Error });
        });

        group.MapGet("/audit-events", async (HttpContext context, IUserAdministrationService service, CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetAuditEventsAsync(ct)) : Results.Forbid());

        app.MapPost("/invitations/redeem", async (
            RedeemInvitationRequest request,
            IUserAdministrationService service,
            CancellationToken ct) =>
        {
            var result = await service.RedeemInvitationAsync(request, ct);
            return result.Error is null
                ? Results.Ok(result.User)
                : Results.BadRequest(new { code = result.Error });
        }).AllowAnonymous().RequireRateLimiting("invite").WithTags("Invitations");
    }
}
