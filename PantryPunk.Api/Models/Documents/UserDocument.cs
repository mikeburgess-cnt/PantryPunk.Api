using System.Text.Json.Serialization;

namespace PantryPunk.Api.Models.Documents;

public class UserDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = null!;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = null!;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("isSubscriber")]
    public bool IsSubscriber { get; set; }

    [JsonPropertyName("subscribedAt")]
    public DateTime? SubscribedAt { get; set; }

    [JsonPropertyName("subscriptionExpiresAt")]
    public DateTime? SubscriptionExpiresAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
