using System.Text.Json.Serialization;

namespace PantryPunk.Api.Models.Documents;

public class ShoppingItemDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("description")]
    public string Description { get; set; } = null!;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; } = null!;

    [JsonPropertyName("knownAs")]
    public string? KnownAs { get; set; } = null!;

    [JsonPropertyName("size")]
    public string? Size { get; set; } = null!;

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    [JsonPropertyName("addedBy")]
    public string AddedBy { get; set; } = null!;

    [JsonPropertyName("addedByMethod")]
    public string AddedByMethod { get; set; } = null!;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("photoUrl")]
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Claude's self-assessed confidence: "high", "medium", or "low".
    /// Null for items added manually or by voice.
    /// </summary>
    [JsonPropertyName("confidence")]
    public string? Confidence { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("isPurchased")]
    public bool IsPurchased { get; set; } = false;
}
