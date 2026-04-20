namespace PantryPunk.Api.Models.Responses;

/// <summary>
/// Response for POST /api/share/generate-code.
/// </summary>
public class GenerateShareCodeResponse
{
    public string ShareId { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string? RecipientName { get; set; }
    public bool Confirmed { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Response item for GET /api/share (shared users list).
/// </summary>
public class ShareCodeResponse
{
    public string ShareId { get; set; } = null!;
    public string? RecipientName { get; set; }
    public string Code { get; set; } = null!;
    public bool Confirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
