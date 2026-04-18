using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/features")]
[Authorize(Policy = "RegisteredUser")]
public class FeatureController : ControllerBase
{
    private readonly FeatureFlagService _featureFlagService;
    private readonly UserService _userService;

    public FeatureController(FeatureFlagService featureFlagService, UserService userService)
    {
        _featureFlagService = featureFlagService;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeatures()
    {
        var userId = User.GetUserId();
        var userDoc = await _userService.GetDocumentAsync(userId);
        var isSubscriber = userDoc?.IsSubscriber ?? false;

        var flags = await _featureFlagService.GetFlagsAsync(userId, isSubscriber);
        return Ok(flags);
    }
}
