using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Services;
using System.Drawing.Drawing2D;
using System.Security.AccessControl;

namespace PantryPunk.Api.Controllers;

[ApiController]
[Route("api/list/items")]
[Authorize]
public class ImageController : ControllerBase
{
    private readonly ImageRecognitionService _imageRecognitionService;
    private readonly BlobStorageService _blobStorageService;
    private readonly ListService _listService;

    public ImageController(
        ImageRecognitionService imageRecognitionService,
        BlobStorageService blobStorageService,
        ListService listService)
    {
        _imageRecognitionService = imageRecognitionService;
        _blobStorageService = blobStorageService;
        _listService = listService;
    }

    [HttpPost("photo")]
    [EnableRateLimiting("ai")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [ProducesResponseType<ShoppingItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadPhoto(IFormFile image)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new ErrorResponse { Error = "No image provided." });

        if (image.ContentType != "image/jpeg")
            return BadRequest(new ErrorResponse { Error = "Only JPEG images are accepted." });

        if (image.Length > 5 * 1024 * 1024)
            return BadRequest(new ErrorResponse { Error = "Image must be under 2MB." });

        var userId = User.GetUserId();

        // Upload to blob storage
        var blobName = $"{userId}/{Guid.NewGuid()}.jpg";
        string blobUrl;
        using (var stream = image.OpenReadStream())
        {
            blobUrl = await _blobStorageService.UploadAsync(blobName, stream, "image/jpeg");
        }

        // Read image bytes for Claude
        byte[] imageBytes;
        using (var memoryStream = new MemoryStream())
        {
            using var stream = image.OpenReadStream();
            await stream.CopyToAsync(memoryStream);
            imageBytes = memoryStream.ToArray();
        }

        // Call Claude for recognition
        var recognition = await _imageRecognitionService.RecogniseAsync(imageBytes);
        if (recognition == null)
        {
            await _blobStorageService.DeleteAsync(blobName);
            return UnprocessableEntity(new ErrorResponse { Error = "Could not recognise item" });
        }

        // Resolve addedBy
        var addedBy = await _listService.ResolveAddedByAsync(userId, User.GetRecipientName());
        if (addedBy == null)
        {
            await _blobStorageService.DeleteAsync(blobName);
            return NotFound(new ErrorResponse { Error = "User not found." });
        }

        // Add item to list
        var list = await _listService.GetListAsync(userId);
        if (list == null)
        {
            await _blobStorageService.DeleteAsync(blobName);
            return NotFound(new ErrorResponse { Error = "Shopping list not found." });
        }

        var now = DateTime.UtcNow;

        string description = recognition.KnownAs
            ?? recognition.Description
            ?? recognition.Brand
            ?? string.Empty;

        var itemDoc = new ShoppingItemDocument
        {
            Id = Guid.NewGuid().ToString(),
            Description = recognition.Description ?? string.Empty,
            Brand = recognition.Brand,
            KnownAs= recognition.KnownAs,
            Quantity = recognition.Quantity,
            Size = recognition.Size,
            AddedBy = addedBy,
            AddedByMethod = "Photo",
            PhotoUrl = blobUrl,
            Confidence = recognition.Confidence,
            CreatedAt = now,
            UpdatedAt = now
        };

        var result = await _listService.AddItemDirectAsync(userId, itemDoc);
        if (result == null)
        {
            await _blobStorageService.DeleteAsync(blobName);
            return NotFound(new ErrorResponse { Error = "Shopping list not found." });
        }

        return Created($"/api/list/items/{result.Id}", result);
    }
}
