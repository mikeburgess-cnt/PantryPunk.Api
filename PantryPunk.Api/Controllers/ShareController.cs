using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/share")]
public class ShareController : ControllerBase
{
    private readonly ShareService _shareService;

    public ShareController(ShareService shareService)
    {
        _shareService = shareService;
    }

    [HttpPost("generate-code")]
    [Authorize(Policy = "RegisteredUser")]
    public async Task<IActionResult> GenerateCode([FromBody] GenerateShareCodeRequest request)
    {
        var recipientName = request.RecipientName?.Trim();
        if (string.IsNullOrEmpty(recipientName))
            return BadRequest(new ErrorResponse { Error = "Recipient name is required." });

        request.RecipientName = recipientName;

        try
        {
            var userId = User.GetUserId();
            var result = await _shareService.GenerateCodeAsync(userId, request);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = ex.Message });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ErrorResponse { Error = ex.Message });
        }
    }

    [HttpPost("confirm-code")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmCode([FromBody] ConfirmShareCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new ErrorResponse { Error = "Code is required." });

        var (success, recipientName, error) = await _shareService.ConfirmCodeAsync(request);

        if (!success)
            return StatusCode(410, new { success = false, error });

        return Ok(new { success = true, recipientName });
    }

    [HttpGet]
    [Authorize(Policy = "RegisteredUser")]
    public async Task<IActionResult> GetShareCodes()
    {
        var userId = User.GetUserId();
        var result = await _shareService.GetShareCodesAsync(userId);
        return Ok(new { sharedUsers = result });
    }

    [HttpDelete("{shareId}")]
    [Authorize(Policy = "RegisteredUser")]
    public async Task<IActionResult> RevokeShareCode(string shareId)
    {
        var userId = User.GetUserId();
        var revoked = await _shareService.RevokeAsync(shareId, userId);

        if (!revoked)
            return NotFound(new ErrorResponse { Error = "Share code not found." });

        return NoContent();
    }
}
