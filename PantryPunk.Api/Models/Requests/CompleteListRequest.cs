namespace PantryPunk.Api.Models.Requests;

public class CompleteListRequest
{
    public List<string> UnboughtItemIds { get; set; } = new();
}
