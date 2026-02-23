using System.Security.Claims;

namespace Household.Api.Helpers;

public static class HttpContextHelper
{
    public static Guid? GetUserId(this HttpContext context)
    {
        var claim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public static bool IsAdmin(this HttpContext context)
    {
        return context.User?.IsInRole("Admin") ?? false;
    }
}
