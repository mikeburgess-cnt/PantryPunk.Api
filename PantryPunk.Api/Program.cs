using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

// Kestrel max request body size: 2MB payload + ~64KB multipart overhead.
// Keeps transport slightly above the 2MB business limit so the validator
// can return a 400 "under 2MB" rather than Kestrel returning 413 first.
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 2 * 1024 * 1024 + 64 * 1024);
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

app.UseHttpsRedirection();

// API key validation (temporary — will be replaced by OAuth)
app.UseMiddleware<ApiKeyMiddleware>();

// Azure App Configuration refresh (must be before UseRouting)
if (!string.IsNullOrEmpty(builder.Configuration["AzureAppConfiguration:Endpoint"]))
{
    app.UseAzureAppConfiguration();
}

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
