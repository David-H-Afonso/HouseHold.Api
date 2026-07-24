using System.Security.Claims;
using System.Text.Json;
using Household.Api.Middleware;
using Household.Api.Models.Auth;
using Microsoft.AspNetCore.Http;

namespace Household.Api.Tests;

public sealed class PasswordChangeRequiredMiddlewareTests
{
    [Theory]
    [InlineData("/auth/me")]
    [InlineData("/auth/change-password")]
    [InlineData("/auth/logout")]
    [InlineData("/auth/logout-all")]
    [InlineData("/health")]
    public async Task RequiredPasswordChange_AllowsOnlyRecoveryEndpoints(string path)
    {
        var nextCalled = false;
        var middleware = new PasswordChangeRequiredMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateAuthenticatedContext(path, requiresPasswordChange: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/admin/users")]
    [InlineData("/api/v1/preferences")]
    [InlineData("/modules/apps")]
    [InlineData("/food-items")]
    public async Task RequiredPasswordChange_BlocksNormalEndpointsWithSafeCode(string path)
    {
        var nextCalled = false;
        var middleware = new PasswordChangeRequiredMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateAuthenticatedContext(path, requiresPasswordChange: true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            PasswordChangeRequiredMiddleware.ErrorCode,
            response.RootElement.GetProperty("code").GetString()
        );
    }

    [Fact]
    public async Task NormalSession_IsNotRestricted()
    {
        var nextCalled = false;
        var middleware = new PasswordChangeRequiredMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateAuthenticatedContext("/admin/users", requiresPasswordChange: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string path, bool requiresPasswordChange)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        HouseholdClaimTypes.RequiresPasswordChange,
                        requiresPasswordChange.ToString().ToLowerInvariant()
                    ),
                ],
                authenticationType: "Test"
            )
        );
        return context;
    }
}
