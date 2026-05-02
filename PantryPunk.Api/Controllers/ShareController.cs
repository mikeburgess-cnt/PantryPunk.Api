using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [ProducesResponseType<GenerateShareCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GenerateCode([FromBody] GenerateShareCodeRequest? request)
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _shareService.GenerateCodeAsync(userId, request ?? new GenerateShareCodeRequest());
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
    [EnableRateLimiting("share-confirm")]
    [ProducesResponseType<ShareCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> ConfirmCode([FromBody] ConfirmShareCodeRequest request)
    {
        request.RecipientName = request.RecipientName.Trim();

        var (response, error) = await _shareService.ConfirmCodeAsync(request);

        if (response == null)
            return StatusCode(410, new ErrorResponse { Error = error! });

        return Ok(response);
    }

    [HttpGet]
    [Authorize(Policy = "RegisteredUser")]
    [ProducesResponseType<SharedUsersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetShareCodes()
    {
        try
        {
            var userId = User.GetUserId();
            var result = await _shareService.GetShareCodesAsync(userId);
            return Ok(new SharedUsersResponse { SharedUsers = result });
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = ex.Message });
        }
    }

    [HttpPatch("{shareId}")]
    [Authorize(Policy = "RegisteredUser")]
    [ProducesResponseType<ShareCodeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateRecipientName(
        string shareId, [FromBody] UpdateShareCodeRecipientNameRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            var (response, error, statusCode) =
                await _shareService.UpdateRecipientNameAsync(shareId, userId, request);

            if (response == null)
                return StatusCode(statusCode!.Value, new ErrorResponse { Error = error! });

            return Ok(response);
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = ex.Message });
        }
    }

    [HttpDelete("{shareId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeShareCode(string shareId)
    {
        try
        {
            var userId = User.GetUserId();
            var isGuest = User.IsShareCodeUser();
            var authedShareId = User.GetShareId();

            var revoked = await _shareService.RevokeAsync(shareId, userId, isGuest, authedShareId);

            if (!revoked)
                return NotFound(new ErrorResponse { Error = "Share code not found." });

            return NoContent();
        }
        catch (ForbiddenException ex)
        {
            return StatusCode(403, new ErrorResponse { Error = ex.Message });
        }
    }
}
