namespace PantryPunk.Api.Models.Responses;

/// <summary>
/// Response for GET /api/household/members. Owner is always the first element.
/// </summary>
public class HouseholdMembersResponse
{
    public List<HouseholdMemberResponse> Members { get; set; } = new();
}

public class HouseholdMemberResponse
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsCurrentUser { get; set; }
    public string? ShareId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}
