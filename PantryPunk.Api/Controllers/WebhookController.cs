using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly UserService _userService;
    private readonly ShareRepository _shareRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        UserService userService,
        ShareRepository shareRepository,
        IConfiguration configuration,
        ILogger<WebhookController> logger)
    {
        _userService = userService;
        _shareRepository = shareRepository;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("revenuecat")]
    public async Task<IActionResult> RevenueCat(
        [FromHeader(Name = "X-RevenueCat-Signature")] string? signature)
    {
        // Read body for signature verification
        Request.EnableBuffering();
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        Request.Body.Position = 0;

        // Verify signature
        var secret = _configuration["RevenueCat:WebhookSecret"];
        if (!string.IsNullOrEmpty(secret))
        {
            if (string.IsNullOrEmpty(signature))
                return Unauthorized();

            var expectedSignature = ComputeHmacSha256(body, secret);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSignature)))
            {
                return Unauthorized();
            }
        }

        // Parse payload
        JsonElement payload;
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (!payload.TryGetProperty("event", out var eventObj))
            return Ok(); // Unknown payload shape — acknowledge

        var eventType = eventObj.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var appUserId = eventObj.TryGetProperty("app_user_id", out var userIdEl) ? userIdEl.GetString() : null;

        if (string.IsNullOrEmpty(appUserId))
        {
            _logger.LogWarning("RevenueCat webhook missing app_user_id");
            return Ok();
        }

        _logger.LogInformation("RevenueCat webhook received: {EventType} for {UserId}", eventType, appUserId);

        var userDoc = await _userService.GetDocumentAsync(appUserId);
        if (userDoc == null)
        {
            _logger.LogWarning("RevenueCat webhook for unknown user: {UserId}", appUserId);
            return Ok();
        }

        switch (eventType)
        {
            case "INITIAL_PURCHASE":
                userDoc.IsSubscriber = true;
                userDoc.SubscribedAt = DateTime.UtcNow;
                userDoc.UpdatedAt = DateTime.UtcNow;
                break;

            case "RENEWAL":
                userDoc.IsSubscriber = true;
                userDoc.SubscriptionExpiresAt = null;
                userDoc.UpdatedAt = DateTime.UtcNow;
                break;

            case "CANCELLATION":
                // Access remains active until EXPIRATION
                if (eventObj.TryGetProperty("expiration_at_ms", out var expirationEl))
                {
                    var expirationMs = expirationEl.GetInt64();
                    userDoc.SubscriptionExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expirationMs).UtcDateTime;
                    userDoc.UpdatedAt = DateTime.UtcNow;
                }
                break;

            case "EXPIRATION":
                userDoc.IsSubscriber = false;
                userDoc.UpdatedAt = DateTime.UtcNow;
                // Revoke all active share codes
                await RevokeAllShareCodesAsync(appUserId);
                break;

            case "BILLING_ISSUE":
                _logger.LogWarning("Billing issue for user {UserId}", appUserId);
                break;

            default:
                _logger.LogInformation("Unhandled RevenueCat event type: {EventType}", eventType);
                return Ok();
        }

        await _userService.UpdateDocumentAsync(userDoc);
        return Ok();
    }

    private async Task RevokeAllShareCodesAsync(string userId)
    {
        var codes = await _shareRepository.GetByOwnerUserIdAsync(userId);
        foreach (var code in codes)
        {
            if (!code.RevokedAt.HasValue)
            {
                code.RevokedAt = DateTime.UtcNow;
                await _shareRepository.ReplaceAsync(code);
            }
        }
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
