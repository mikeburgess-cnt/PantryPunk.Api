using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Middleware;

public class AppConfigMiddleware
{
    private readonly RequestDelegate _next;

    public AppConfigMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var repository = context.RequestServices.GetRequiredService<AppConfigRepository>();
        var settings = await repository.GetSettingsAsync();

        AmbientConfigurationProvider.Current = settings;
        try
        {
            await _next(context);
        }
        finally
        {
            AmbientConfigurationProvider.Current = null;
        }
    }
}
