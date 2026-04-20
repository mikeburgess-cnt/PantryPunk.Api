using System.Security.Claims;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Middleware;

public class ShareCodeAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ShareCodeAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if already authenticated via JWT
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        // Check for X-Share-Code header
        if (!context.Request.Headers.TryGetValue("X-Share-Code", out var shareCodeHeader))
        {
            await _next(context);
            return;
        }

        var code = shareCodeHeader.ToString().Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            await _next(context);
            return;
        }

        var shareRepository = context.RequestServices.GetRequiredService<ShareRepository>();
        var document = await shareRepository.GetByCodeAsync(code);

        if (document == null || document.RevokedAt.HasValue)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid share code" });
            return;
        }

        if (!document.Confirmed)
        {
            if (document.ExpiresAt < DateTime.UtcNow)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Share code has expired" });
                return;
            }

            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Share code has not been confirmed" });
            return;
        }

        // Inject synthetic claims principal with owner's userId, recipient name, and share code id
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, document.OwnerUserId),
            new Claim("RecipientName", document.RecipientName!),
            new Claim("ShareId", document.Id)
        };

        var identity = new ClaimsIdentity(claims, "ShareCode");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
