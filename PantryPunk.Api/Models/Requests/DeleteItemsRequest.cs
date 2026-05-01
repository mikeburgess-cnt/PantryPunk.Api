namespace PantryPunk.Api.Models.Requests;

public class DeleteItemsRequest
{
    public List<string> ItemIds { get; set; } = new();
}
