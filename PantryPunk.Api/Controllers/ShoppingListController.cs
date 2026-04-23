using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/shopping-list")]
[Authorize]
public class ShoppingListController : ControllerBase
{
    private readonly ListService _listService;
    private readonly UserService _userService;

    public ShoppingListController(ListService listService, UserService userService)
    {
        _listService = listService;
        _userService = userService;
    }

    [HttpGet]
    [ProducesResponseType<ShoppingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetList()
    {
        await _userService.EnsureExistsAsync(User);
        var userId = User.GetUserId();
        var result = await _listService.GetListAsync(userId);
        return Ok(result);
    }

    [HttpPost("items")]
    [ProducesResponseType<ShoppingItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest request)
    {
        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(description))
            return BadRequest(new ErrorResponse { Error = "Description is required." });

        request.Description = description;

        await _userService.EnsureExistsAsync(User);
        var userId = User.GetUserId();
        await _listService.GetOrCreateActiveAsync(userId);

        var addedBy = await _listService.ResolveAddedByAsync(userId, User.GetRecipientName());
        if (addedBy == null)
            return NotFound(new ErrorResponse { Error = "User not found." });

        var result = await _listService.AddItemAsync(userId, request, addedBy);
        if (result == null)
            return NotFound(new ErrorResponse { Error = "Shopping list not found." });

        return Created($"/api/list/items/{result.Id}", result);
    }

    [HttpPut("items/{itemId}")]
    [ProducesResponseType<ShoppingItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateItem(string itemId, [FromBody] UpdateItemRequest request)
    {
        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(description))
            return BadRequest(new ErrorResponse { Error = "Description is required." });

        request.Description = description;

        await _userService.EnsureExistsAsync(User);
        var userId = User.GetUserId();
        var result = await _listService.UpdateItemAsync(userId, itemId, request);

        if (result == null)
            return NotFound(new ErrorResponse { Error = "Item not found." });

        return Ok(result);
    }

    [HttpPost("complete")]
    [ProducesResponseType<ShoppingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Complete()
    {
        await _userService.EnsureExistsAsync(User);
        var userId = User.GetUserId();
        try
        {
            var newList = await _listService.CompleteAsync(userId);
            if (newList == null)
                return NotFound(new ErrorResponse { Error = "Shopping list not found." });

            return Ok(newList);
        }
        catch (EmptyListException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
    }

    [HttpDelete("items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteItem(string itemId)
    {
        await _userService.EnsureExistsAsync(User);
        var userId = User.GetUserId();
        var deleted = await _listService.DeleteItemAsync(userId, itemId);

        if (!deleted)
            return NotFound(new ErrorResponse { Error = "Item not found." });

        return NoContent();
    }
}
