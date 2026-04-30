namespace PantryPunk.Api.Models.Responses;

public class SharedUsersResponse
{
    public List<ShareCodeResponse> SharedUsers { get; set; } = new();
}
