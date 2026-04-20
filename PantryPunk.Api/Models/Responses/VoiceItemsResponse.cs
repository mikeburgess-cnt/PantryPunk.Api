namespace PantryPunk.Api.Models.Responses;

public class VoiceItemsResponse
{
    public List<ShoppingItemResponse> Items { get; set; } = new();
}
