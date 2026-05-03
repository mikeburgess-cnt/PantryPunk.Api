using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Infrastructure;
using PantryPunk.Api.Middleware;
using PantryPunk.Api.Models.Responses;

var builder = WebApplication.CreateBuilder(args);

// Secrets from Key Vault (no-op locally if KeyVault:Uri is unset)
builder.AddKeyVaultSecrets();

// Per-request configuration overlay populated by AppConfigMiddleware from Cosmos.
// Sits last in the IConfigurationRoot chain so its values override appsettings/env vars.
((IConfigurationBuilder)builder.Configuration).Add(new AmbientConfigurationProvider());

builder.Services.AddMemoryCache();
builder.Services.AddFeatureManagement();

// Auth0 JWT Authentication
var auth0Domain = builder.Configuration["Auth0:Domain"];
var auth0Audience = builder.Configuration["Auth0:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Domain}/";
        options.Audience = auth0Audience;
        options.IncludeErrorDetails = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0");
                var authHeader = ctx.Request.Headers.Authorization.ToString();
                var hasBearer = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
                logger.LogInformation(
                    "JWT message received for {Method} {Path}. AuthHeader length: {HeaderLen}. Bearer prefix: {HasBearer}. Token length: {TokenLen}",
                    ctx.Request.Method,
                    ctx.Request.Path,
                    authHeader.Length,
                    hasBearer,
                    hasBearer ? authHeader.Length - 7 : 0);
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0");
                var messages = new List<string>();
                for (var ex = ctx.Exception; ex != null; ex = ex.InnerException)
                {
                    messages.Add($"{ex.GetType().Name}: {ex.Message}");
                }
                logger.LogWarning(ctx.Exception,
                    "JWT auth failed for {Path}. Authority={Authority} Audience={Audience}. Chain: {Chain}",
                    ctx.Request.Path,
                    $"https://{auth0Domain}/",
                    auth0Audience,
                    string.Join(" -> ", messages));
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0");
                logger.LogWarning(
                    "JWT challenge issued for {Method} {Path}. Error: {Error}. Description: {Description}. AuthFailure: {AuthFailure}",
                    ctx.Request.Method,
                    ctx.Request.Path,
                    ctx.Error,
                    ctx.ErrorDescription,
                    ctx.AuthenticateFailure?.Message);
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

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    // Per-user AI limiter (image endpoint)
    options.AddPolicy("ai", context =>
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("PantryPunk:RateLimit:AiRequestsPerMinute", 30),
            Window = TimeSpan.FromMinutes(1)
        });
    });

    // Tight per-IP limiter for share-code confirm (unauthenticated, brute-force surface)
    options.AddPolicy("share-confirm", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"sc:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("PantryPunk:RateLimit:ShareConfirmPerHour", 10),
            Window = TimeSpan.FromHours(1)
        });
    });

    // Per-principal limiter for share-code rename. Partition by ShareId for guests
    // (so each guest gets their own bucket) and by NameIdentifier for JWT subscribers.
    options.AddPolicy("share-update", context =>
    {
        var shareId = context.User.FindFirst("ShareId")?.Value;
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = shareId != null ? $"su:g:{shareId}" : $"su:u:{userId ?? "anonymous"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("PantryPunk:RateLimit:ShareUpdatePerHour", 10),
            Window = TimeSpan.FromHours(1)
        });
    });

    // Global catch-all per-IP limiter — requires H3 (ForwardedHeaders) for real client IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"g:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("PantryPunk:RateLimit:PerIpPerMinute", 120),
            Window = TimeSpan.FromMinutes(1)
        });
    });
});

// Kestrel max request body size: 3MB payload + ~64KB multipart overhead, sized for the image upload limit.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 3 * 1024 * 1024 + 64 * 1024);
var app = builder.Build();

// One-shot startup log so we can confirm in App Insights what Auth0 values
// the running instance was actually configured with.
app.Logger.LogWarning(
    "Auth0 config at startup: Authority=https://{Domain}/ Audience={Audience} DomainPresent={DomainPresent} AudiencePresent={AudiencePresent}",
    auth0Domain,
    auth0Audience,
    !string.IsNullOrWhiteSpace(auth0Domain),
    !string.IsNullOrWhiteSpace(auth0Audience));

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

// Per-request AppConfig overlay must run before anything that reads IConfiguration overlay values.
app.UseMiddleware<AppConfigMiddleware>();

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
app.MapHealthChecks("/health").DisableRateLimiting();

app.Run();
