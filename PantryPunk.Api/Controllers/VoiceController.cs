using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/list/items")]
[Authorize]
public class VoiceController : ControllerBase
{
    private readonly VoiceRecognitionService _voiceRecognitionService;
    private readonly ListService _listService;

    public VoiceController(VoiceRecognitionService voiceRecognitionService, ListService listService)
    {
        _voiceRecognitionService = voiceRecognitionService;
        _listService = listService;
    }

    [HttpPost("voice")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> UploadVoice(IFormFile audio)
    {
        if (audio == null || audio.Length == 0)
            return BadRequest(new ErrorResponse { Error = "No audio provided." });

        if (audio.ContentType is not ("audio/m4a" or "audio/mp4"))
            return BadRequest(new ErrorResponse { Error = "Only m4a/mp4 audio is accepted." });

        if (audio.Length > 2 * 1024 * 1024)
            return BadRequest(new ErrorResponse { Error = "Audio must be under 2MB." });

        var userId = User.GetUserId();

        // Transcribe audio
        string? transcription;
        using (var stream = audio.OpenReadStream())
        {
            transcription = await _voiceRecognitionService.TranscribeAsync(stream);
        }

        if (string.IsNullOrWhiteSpace(transcription))
            return UnprocessableEntity(new ErrorResponse { Error = "Could not transcribe audio" });

        // Extract items via Claude
        var extractedItems = await _voiceRecognitionService.ExtractItemsAsync(transcription);
        if (extractedItems == null || extractedItems.Count == 0)
            return UnprocessableEntity(new ErrorResponse { Error = "Could not recognise items from speech" });

        // Resolve addedBy
        var addedBy = await _listService.ResolveAddedByAsync(userId, User.GetRecipientName());
        if (addedBy == null)
            return NotFound(new ErrorResponse { Error = "User not found." });

        // Build item documents
        var now = DateTime.UtcNow;
        var items = extractedItems.Select(ei => new ShoppingItemDocument
        {
            Id = Guid.NewGuid().ToString(),
            Description = ei.Description.Trim(),
            Quantity = ei.Quantity,
            AddedBy = addedBy,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        // Add all items to list
        var result = await _listService.AddItemsDirectAsync(userId, items);
        if (result == null)
            return NotFound(new ErrorResponse { Error = "Shopping list not found." });

        return Created("/api/list", new { items = result });
    }
}
