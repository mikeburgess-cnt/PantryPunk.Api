namespace PantryPunk.Api.Models.Responses;

public class ShareCodeResponse
{
    public string ShareId { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string RecipientName { get; set; } = null!;
    public bool Confirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
