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
public class ImageController : ControllerBase
{
    private const int UploadSizeLimit = 2 * 1024 * 1024 + 64 * 1024;

    private readonly ImageRecognitionService _imageRecognitionService;
    private readonly BlobStorageService _blobStorageService;
    private readonly ListService _listService;
    private readonly ImageFileValidator _imageFileValidator;

    public ImageController(
        ImageRecognitionService imageRecognitionService,
        BlobStorageService blobStorageService,
        ListService listService,
        ImageFileValidator imageFileValidator)
    {
        _imageRecognitionService = imageRecognitionService;
        _blobStorageService = blobStorageService;
        _listService = listService;
        _imageFileValidator = imageFileValidator;
    }

    [HttpPost("photo")]
    [EnableRateLimiting("ai")]
    [RequestSizeLimit(UploadSizeLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadSizeLimit)]
    [ProducesResponseType<ShoppingItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadPhoto(IFormFile image, CancellationToken ct)
    {
        var validation = await _imageFileValidator.ValidateAsync(image, ct);
        if (!validation.IsValid)
            return BadRequest(new ErrorResponse { Error = validation.Error! });

        var userId = User.GetUserId();
        var imageBytes = validation.Bytes!;
        var mediaType = validation.MediaType!;

        // Upload to blob storage
        var blobName = $"{userId}/{Guid.NewGuid()}.{validation.Extension}";
        string blobUrl;
        using (var stream = new MemoryStream(imageBytes, writable: false))
        {
            blobUrl = await _blobStorageService.UploadAsync(blobName, stream, mediaType);
        }

        // Call Claude for recognition
        var recognition = await _imageRecognitionService.RecogniseAsync(imageBytes, mediaType);
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
