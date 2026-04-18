namespace PantryPunk.Api.Models.Responses;

public class ShoppingItemResponse
{
    public string Id { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int? Quantity { get; set; }
    public string AddedBy { get; set; } = null!;
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
