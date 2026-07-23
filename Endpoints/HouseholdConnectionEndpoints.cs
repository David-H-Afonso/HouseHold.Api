using Household.Api.Application.Services;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class HouseholdConnectionEndpoints
{
    public static void MapHouseholdConnectionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/integrations").WithTags("Household Connections").RequireAuthorization();

        group
            .MapGet(
                "/connections",
                async (HttpContext context, HouseholdConsumerConnectionService service, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    return userId is null
                        ? Results.Unauthorized()
                        : Results.Ok(await service.GetConnectionsAsync(userId.Value, ct));
                }
            )
            .WithName("GetHouseholdConnections");

        group
            .MapPost(
                "/connections/{provider}/authorize",
                async (
                    string provider,
                    HttpContext context,
                    HouseholdConsumerConnectionService service,
                    CancellationToken ct
                ) =>
                {
                    if (provider.Length > 64)
                        return Results.NotFound();
                    var userId = context.GetUserId();
                    if (userId is null)
                        return Results.Unauthorized();

                    var result = await service.AuthorizeAsync(userId.Value, provider, ct);
                    return result.Kind switch
                    {
                        HouseholdAuthorizeResultKind.Success => Results.Ok(
                            new Household.Api.DTOs.HouseholdAuthorizationResponse(result.AuthorizationUrl!)
                        ),
                        HouseholdAuthorizeResultKind.NotConfigured => Results.Conflict(
                            new { message = "provider_not_configured" }
                        ),
                        _ => Results.NotFound(),
                    };
                }
            )
            .RequireRateLimiting("integration-authorize")
            .WithName("AuthorizeHouseholdConnection");

        group
            .MapPost(
                "/connections/{provider}/test",
                async (
                    string provider,
                    HttpContext context,
                    HouseholdConsumerConnectionService service,
                    CancellationToken ct
                ) =>
                {
                    if (provider.Length > 64)
                        return Results.NotFound();
                    var userId = context.GetUserId();
                    if (userId is null)
                        return Results.Unauthorized();

                    var connection = await service.TestAsync(userId.Value, provider, ct);
                    return connection is null ? Results.NotFound() : Results.Ok(connection);
                }
            )
            .WithName("TestHouseholdConnection");

        group
            .MapDelete(
                "/connections/{provider}",
                async (
                    string provider,
                    HttpContext context,
                    HouseholdConsumerConnectionService service,
                    CancellationToken ct
                ) =>
                {
                    if (provider.Length > 64)
                        return Results.NotFound();
                    var userId = context.GetUserId();
                    if (userId is null)
                        return Results.Unauthorized();

                    var result = await service.DisconnectAsync(userId.Value, provider, ct);
                    return result switch
                    {
                        HouseholdDisconnectResult.Success => Results.NoContent(),
                        HouseholdDisconnectResult.NotFound => Results.NotFound(),
                        _ => Results.Json(new { message = "revocation_failed" }, statusCode: StatusCodes.Status502BadGateway),
                    };
                }
            )
            .WithName("DeleteHouseholdConnection");

        app.MapGet(
                "/integrations/callback/{provider}",
                async (
                    string provider,
                    string? code,
                    string? state,
                    string? error,
                    HouseholdConsumerConnectionService service,
                    CancellationToken ct
                ) =>
                {
                    if (provider.Length > 64)
                        return Results.BadRequest(new { message = "invalid_callback" });
                    var result = await service.HandleCallbackAsync(provider, code, state, error, ct);
                    return result.CanRedirect && result.RedirectUrl is not null
                        ? Results.Redirect(result.RedirectUrl)
                        : Results.BadRequest(new { message = "invalid_callback" });
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("integration-callback")
            .WithName("HouseholdConnectionCallback");
    }
}
