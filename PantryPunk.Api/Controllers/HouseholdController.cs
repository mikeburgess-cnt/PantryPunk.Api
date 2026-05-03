using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/household")]
public class HouseholdController : ControllerBase
{
    private readonly HouseholdService _householdService;

    public HouseholdController(HouseholdService householdService)
    {
        _householdService = householdService;
    }

    [HttpGet("members")]
    [Authorize]
    [ProducesResponseType<HouseholdMembersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMembers()
    {
        var userId = User.GetUserId();
        var authenticatedShareId = User.GetShareId();
        var response = await _householdService.GetMembersAsync(userId, authenticatedShareId);

        if (response == null)
            return NotFound(new ErrorResponse { Error = "Household owner not found." });

        return Ok(response);
    }
}
