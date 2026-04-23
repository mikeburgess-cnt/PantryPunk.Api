using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Middleware;
using PantryPunk.Api.Models.Responses;

var builder = WebApplication.CreateBuilder(args);

// Key Vault (must be first so secrets are available to subsequent config)
builder.Configuration.AddKeyVault();

// Azure App Configuration + Feature Management
builder.AddAppConfiguration();

// Auth0 JWT Authentication
var auth0Domain = builder.Configuration["Auth0:Domain"];
var auth0Audience = builder.Configuration["Auth0:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0");
                logger.LogWarning(ctx.Exception,
                    "JWT auth failed: {Message}. AuthHeader present: {HasHeader}",
                    ctx.Exception.Message,
                    ctx.Request.Headers.ContainsKey("Authorization"));
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0");
                logger.LogInformation("JWT validated for sub {Sub}",
                    ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RegisteredUser", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx => !ctx.User.IsShareCodeUser()));
});

// Infrastructure
builder.Services.AddCosmosDb(builder.Configuration);
builder.Services.AddBlobStorage(builder.Configuration);

// HTTP clients for external APIs
builder.Services.AddHttpClient("Claude");
builder.Services.AddHttpClient("AzureSpeech");

// Application services
builder.Services.AddRepositories();
builder.Services.AddAppServices();

// Application Insights
builder.Services.AddApplicationInsightsTelemetry();

// Controllers + OpenAPI
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Health checks
builder.Services.AddHealthChecks();

// HSTS (Strict-Transport-Security)
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// Rate limiting for AI endpoints
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    options.AddPolicy("ai", context =>
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("PantryPunk:RateLimit:AiRequestsPerMinute", 30),
            Window = TimeSpan.FromMinutes(1)
        });
    });
});

// Kestrel max request body size: 3MB payload + ~64KB multipart overhead.
// Sized for the image upload limit (audio validator enforces its own 2MB cap)
// so the validator can return a 400 rather than Kestrel returning 413 first.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 3 * 1024 * 1024 + 64 * 1024);
var app = builder.Build();

// Middleware pipeline order matters
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PantryPunk API");
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";
    // API never renders HTML — strict CSP. Exempt Swagger/OpenAPI paths so Dev UI renders.
    if (!ctx.Request.Path.StartsWithSegments("/swagger") && !ctx.Request.Path.StartsWithSegments("/openapi"))
    {
        h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
    await next();
});

// Azure App Configuration refresh (must be before UseRouting)
if (!string.IsNullOrEmpty(builder.Configuration["AzureAppConfiguration:Endpoint"]))
{
    app.UseAzureAppConfiguration();
}

// Trust the single XFF hop from the Azure App Service front-end proxy.
// KnownNetworks/KnownProxies are cleared so only the platform's XFF hop is unwrapped —
// clients cannot spoof the header by adding their own.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

app.UseAuthentication();
app.UseMiddleware<ShareCodeAuthMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<DevAuthMiddleware>();
}
app.UseAuthorization();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseRateLimiter();

// Global exception handler
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Error = "An unexpected error occurred.",
            TraceId = context.TraceIdentifier
        });
    });
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
