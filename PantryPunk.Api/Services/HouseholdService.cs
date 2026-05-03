using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Services;

public class HouseholdService
{
    private readonly UserService _userService;
    private readonly ShareRepository _shareRepository;

    public HouseholdService(UserService userService, ShareRepository shareRepository)
    {
        _userService = userService;
        _shareRepository = shareRepository;
    }

    public virtual async Task<HouseholdMembersResponse?> GetMembersAsync(string ownerUserId, string? authenticatedShareId)
    {
        var owner = await _userService.GetDocumentAsync(ownerUserId);
        if (owner == null) return null;

        var shareDocs = await _shareRepository.GetByOwnerUserIdAsync(ownerUserId);

        var members = new List<HouseholdMemberResponse>
        {
            new()
            {
                DisplayName = string.IsNullOrWhiteSpace(owner.DisplayName) ? "Owner" : owner.DisplayName,
                IsOwner = true,
                IsCurrentUser = false,
                ShareId = null,
                ConfirmedAt = null
            }
        };

        members.AddRange(shareDocs
            .Where(d => d.Confirmed)
            .Select(d => new HouseholdMemberResponse
            {
                DisplayName = d.RecipientName ?? string.Empty,
                IsOwner = false,
                IsCurrentUser = authenticatedShareId != null && d.Id == authenticatedShareId,
                ShareId = d.Id,
                ConfirmedAt = d.ConfirmedAt
            }));

        return new HouseholdMembersResponse { Members = members };
    }
}
