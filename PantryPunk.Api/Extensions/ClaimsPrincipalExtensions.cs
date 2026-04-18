using System.Security.Claims;

namespace PantryPunk.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim not found.");
    }

    public static string? GetRecipientName(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("RecipientName")?.Value;
    }

    public static bool IsShareCodeUser(this ClaimsPrincipal principal)
    {
        return principal.GetRecipientName() != null;
    }
}
