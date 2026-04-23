using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = "RegisteredUser")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;

    public UserController(UserService userService)
    {
        _userService = userService;
    }

    [HttpPost("profile")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpsertProfile([FromBody] UpdateProfileRequest request)
    {
        request.DisplayName = request.DisplayName.Trim();

        var userId = User.GetUserId();
        var result = await _userService.UpsertProfileAsync(userId, request);
        return Ok(result);
    }

    [HttpGet("profile")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = User.GetUserId();
        var result = await _userService.GetProfileAsync(userId);

        if (result == null)
            return NotFound(new ErrorResponse { Error = "User not found." });

        return Ok(result);
    }

}
