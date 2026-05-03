using System.Security.Claims;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Middleware;

public class ShareCodeAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ShareCodeAuthMiddleware> _logger;

    public ShareCodeAuthMiddleware(RequestDelegate next, ILogger<ShareCodeAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // TEMP DEBUG: remove once share-code bug is resolved
        var shareCodeHeaderValue = context.Request.Headers.TryGetValue("X-Share-Code", out var hdr)
            ? hdr.ToString()
            : "(missing)";
        _logger.LogInformation(
            "ShareCode debug Path={Path} Method={Method} ShareCode={ShareCode} JwtAuthenticated={JwtAuthenticated}",
            context.Request.Path,
            context.Request.Method,
            shareCodeHeaderValue,
            context.User.Identity?.IsAuthenticated == true);

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

        // All invalid states return the same response to prevent code enumeration.
        // Distinct errors (expired vs unconfirmed) would let attackers narrow the search space.
        if (document == null
            || document.RevokedAt.HasValue
            || !document.Confirmed
            || document.ExpiresAt < DateTime.UtcNow)
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = "Invalid share code",
                TraceId = context.TraceIdentifier
            });
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
