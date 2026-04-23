using System.Security.Claims;
using PantryPunk.Api.Extensions;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Services;

public class UserService
{
    private readonly UserRepository _userRepository;
    private readonly ListRepository _listRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(UserRepository userRepository, ListRepository listRepository, ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _listRepository = listRepository;
        _logger = logger;
    }

    public virtual async Task<UserProfileResponse> UpsertProfileAsync(string userId, UpdateProfileRequest request)
    {
        var displayName = request.DisplayName.Trim();
        var now = DateTime.UtcNow;

        var existing = await _userRepository.GetByIdAsync(userId);
        var isNew = existing == null;

        var document = existing ?? new UserDocument
        {
            Id = userId,
            UserId = userId,
            CreatedAt = now
        };

        document.DisplayName = displayName;
        document.Email = request.Email;
        document.UpdatedAt = now;

        await _userRepository.UpsertAsync(document);

        if (isNew)
        {
            try
            {
                var listId = Guid.NewGuid().ToString();
                var list = new ShoppingListDocument
                {
                    Id = listId,
                    ListId = listId,
                    OwnerUserId = userId,
                    Items = new List<ShoppingItemDocument>(),
                    Status = ShoppingListStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _listRepository.CreateAsync(list);
            }
            catch (Exception ex)
            {
                // Per spec: do not return error — list will be created on-the-fly by GET /api/list
                _logger.LogWarning(ex, "Failed to create initial shopping list for user {UserId}", userId);
            }
        }

        return MapToResponse(document);
    }

    public virtual async Task<UserDocument?> EnsureExistsAsync(ClaimsPrincipal principal)
    {
        if (principal.IsShareCodeUser()) return null;

        var userId = principal.GetUserId();
        var existing = await _userRepository.GetByIdAsync(userId);
        if (existing != null) return existing;

        var now = DateTime.UtcNow;
        var document = new UserDocument
        {
            Id = userId,
            UserId = userId,
            DisplayName = principal.GetNameClaim() ?? string.Empty,
            Email = principal.GetEmailClaim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _userRepository.UpsertAsync(document);
        _logger.LogInformation("Auto-created user {UserId} from JWT claims", userId);
        return document;
    }

    public virtual async Task<UserProfileResponse?> GetProfileAsync(string userId)
    {
        var document = await _userRepository.GetByIdAsync(userId);
        return document == null ? null : MapToResponse(document);
    }

    public virtual async Task<UserProfileResponse?> UpdateSubscriptionAsync(string userId, UpdateSubscriptionRequest request)
    {
        var document = await _userRepository.GetByIdAsync(userId);
        if (document == null) return null;

        document.IsSubscriber = request.IsSubscriber;
        document.UpdatedAt = DateTime.UtcNow;

        if (request.IsSubscriber && document.SubscribedAt == null)
            document.SubscribedAt = DateTime.UtcNow;

        await _userRepository.UpsertAsync(document);
        return MapToResponse(document);
    }

    public virtual async Task<UserDocument?> GetDocumentAsync(string userId)
    {
        return await _userRepository.GetByIdAsync(userId);
    }

    public virtual async Task UpdateDocumentAsync(UserDocument document)
    {
        await _userRepository.UpsertAsync(document);
    }

    public virtual async Task RequireSubscriberAsync(string userId)
    {
        var document = await _userRepository.GetByIdAsync(userId);
        if (document == null || !document.IsSubscriber)
            throw new ForbiddenException("Sharing requires an active subscription.");
    }

    private static UserProfileResponse MapToResponse(UserDocument document)
    {
        return new UserProfileResponse
        {
            Id = document.UserId,
            DisplayName = document.DisplayName,
            IsSubscriber = document.IsSubscriber
        };
    }
}

public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
