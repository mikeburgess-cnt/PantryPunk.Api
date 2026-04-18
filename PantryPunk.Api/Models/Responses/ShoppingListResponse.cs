namespace PantryPunk.Api.Models.Responses;

public class ShoppingListResponse
{
    public string ListId { get; set; } = null!;
    public List<ShoppingItemResponse> Items { get; set; } = new();
}
