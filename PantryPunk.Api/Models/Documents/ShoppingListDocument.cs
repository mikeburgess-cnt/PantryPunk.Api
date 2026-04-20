using System.Text.Json.Serialization;

namespace PantryPunk.Api.Models.Documents;

public class ShoppingListDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("listId")]
    public string ListId { get; set; } = null!;

    [JsonPropertyName("ownerUserId")]
    public string OwnerUserId { get; set; } = null!;

    [JsonPropertyName("items")]
    public List<ShoppingItemDocument> Items { get; set; } = new();

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTime? CompletedAt { get; set; }
}
