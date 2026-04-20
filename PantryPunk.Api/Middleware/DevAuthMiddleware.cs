using System.Security.Claims;

namespace PantryPunk.Api.Middleware;

// Temporary dev-only shim that hard-codes a user identity until Auth0 is wired up.
// Registered only in Development (see Program.cs); reads PantryPunk:DevAuth:UserId from config.
public class DevAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public DevAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var devUserId = _configuration["PantryPunk:DevAuth:UserId"];
        if (string.IsNullOrWhiteSpace(devUserId))
        {
            await _next(context);
            return;
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, devUserId) },
            authenticationType: "DevAuth");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }
}
