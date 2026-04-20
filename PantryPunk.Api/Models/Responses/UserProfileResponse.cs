namespace PantryPunk.Api.Models.Responses;

public class UserProfileResponse
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsSubscriber { get; set; }
}
