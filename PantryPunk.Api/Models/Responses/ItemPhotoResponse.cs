namespace PantryPunk.Api.Models.Responses;

public class ItemPhotoResponse
{
    public string? PhotoUrl { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
