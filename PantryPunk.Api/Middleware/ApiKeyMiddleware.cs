using PantryPunk.Api.Models.Responses;

namespace PantryPunk.Api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    // 64-char hex key — replace before deploying, and move to config/secrets when OAuth is added
    private const string ValidApiKey = "a1f8c3d9e7b24560f3a9d8c1e5b7024f6d3a8e1c9b5f2074d6e9a3c1b8f50d2e";

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health"
    };

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip validation for excluded paths and OpenAPI/Swagger in development
        if (ExcludedPaths.Contains(path) || path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
            !string.Equals(providedKey.ToString(), ValidApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Error = "Invalid or missing API key.",
                TraceId = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
