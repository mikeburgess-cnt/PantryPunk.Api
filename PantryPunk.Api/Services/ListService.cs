using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Services;

public class ListService
{
    private readonly ListRepository _listRepository;
    private readonly UserRepository _userRepository;
    private readonly BlobSasTokenService _sasTokenService;

    public ListService(ListRepository listRepository, UserRepository userRepository, BlobSasTokenService sasTokenService)
    {
        _listRepository = listRepository;
        _userRepository = userRepository;
        _sasTokenService = sasTokenService;
    }

    public async Task<ShoppingListResponse> GetListAsync(string userId)
    {
        var list = await GetOrCreateActiveAsync(userId);
        return MapToResponse(list);
    }

    public async Task<ShoppingListDocument> GetOrCreateActiveAsync(string userId)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list != null) return list;

        var now = DateTime.UtcNow;
        var listId = Guid.NewGuid().ToString();
        var newList = new ShoppingListDocument
        {
            Id = listId,
            ListId = listId,
            OwnerUserId = userId,
            Items = new List<ShoppingItemDocument>(),
            Status = ShoppingListStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _listRepository.CreateAsync(newList);
        return newList;
    }

    public async Task<ShoppingItemResponse?> AddItemAsync(string userId, AddItemRequest request, string addedBy)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list == null) return null;

        var now = DateTime.UtcNow;
        var item = new ShoppingItemDocument
        {
            Id = Guid.NewGuid().ToString(),
            Description = request.Description.Trim(),
            Quantity = request.Quantity,
            AddedBy = addedBy,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        list.Items.Add(item);
        list.UpdatedAt = now;
        await _listRepository.ReplaceAsync(list);

        return MapItemToResponse(item);
    }

    public async Task<ShoppingItemResponse?> UpdateItemAsync(string userId, string itemId, UpdateItemRequest request)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list == null) return null;

        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return null;

        var now = DateTime.UtcNow;
        item.Description = request.Description.Trim();
        item.Quantity = request.Quantity;
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        item.UpdatedAt = now;

        list.UpdatedAt = now;
        await _listRepository.ReplaceAsync(list);

        return MapItemToResponse(item);
    }

    public async Task<bool> DeleteItemAsync(string userId, string itemId)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list == null) return false;

        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return false;

        list.Items.Remove(item);
        list.UpdatedAt = DateTime.UtcNow;
        await _listRepository.ReplaceAsync(list);

        return true;
    }

    public async Task<ShoppingItemResponse?> AddItemDirectAsync(string userId, ShoppingItemDocument itemDoc)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list == null) return null;

        list.Items.Add(itemDoc);
        list.UpdatedAt = itemDoc.UpdatedAt;
        await _listRepository.ReplaceAsync(list);

        return MapItemToResponse(itemDoc);
    }

    public async Task<List<ShoppingItemResponse>?> AddItemsDirectAsync(string userId, List<ShoppingItemDocument> items)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (list == null) return null;

        list.Items.AddRange(items);
        list.UpdatedAt = DateTime.UtcNow;
        await _listRepository.ReplaceAsync(list);

        return items.Select(MapItemToResponse).ToList();
    }

    // Create-new-first ordering: if the second write (marking the old list completed) fails,
    // a retry sees the new empty list and rejects with EmptyListException. The failure leaves
    // two active-flagged documents in the same partition; GetActiveByOwnerUserIdAsync breaks
    // the tie by createdAt DESC so the newer empty list wins. Operator cleanup is still
    // advisable to restore the single-active invariant.
    public async Task<ShoppingListResponse?> CompleteAsync(string userId)
    {
        var active = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        if (active == null) return null;

        if (active.Items.Count == 0)
            throw new EmptyListException("Cannot complete an empty list.");

        var now = DateTime.UtcNow;
        var newList = new ShoppingListDocument
        {
            Id = Guid.NewGuid().ToString(),
            ListId = active.ListId,
            OwnerUserId = userId,
            Items = new List<ShoppingItemDocument>(),
            Status = ShoppingListStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _listRepository.CreateAsync(newList);

        active.Status = ShoppingListStatus.Completed;
        active.CompletedAt = now;
        active.UpdatedAt = now;
        await _listRepository.ReplaceAsync(active);

        return MapToResponse(newList);
    }

    public async Task<ItemPhotoResponse?> GetItemPhotoAsync(string userId, string itemId)
    {
        var list = await _listRepository.GetActiveByOwnerUserIdAsync(userId);
        var item = list?.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null || string.IsNullOrEmpty(item.PhotoUrl)) return null;

        var sas = await _sasTokenService.GetReadSasUrlAsync(item.PhotoUrl, TimeSpan.FromHours(1));
        return new ItemPhotoResponse
        {
            PhotoUrl = sas?.Url,
            ExpiresAt = sas?.ExpiresAt
        };
    }

    public async Task<string?> ResolveAddedByAsync(string userId, string? recipientName)
    {
        if (recipientName != null)
            return recipientName;

        var user = await _userRepository.GetByIdAsync(userId);
        return user?.DisplayName;
    }

    private static ShoppingListResponse MapToResponse(ShoppingListDocument document)
    {
        return new ShoppingListResponse
        {
            ListId = document.ListId,
            Items = document.Items.Select(MapItemToResponse).ToList()
        };
    }

    private static ShoppingItemResponse MapItemToResponse(ShoppingItemDocument item)
    {
        return new ShoppingItemResponse
        {
            Id = item.Id,
            Description = item.Description,
            Brand = item.Brand,
            KnownAs = item.KnownAs,
            Size = item.Size,
            Quantity = item.Quantity,
            AddedBy = item.AddedBy,
            AddedByMethod = item.AddedByMethod,
            Notes = item.Notes,
            HasPhoto = !string.IsNullOrEmpty(item.PhotoUrl),
            Confidence = item.Confidence,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }
}
