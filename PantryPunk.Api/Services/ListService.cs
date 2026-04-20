using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Models.Responses;
using PantryPunk.Api.Repositories;

namespace PantryPunk.Api.Services;

public class ListService
{
    private readonly ListRepository _listRepository;
    private readonly UserRepository _userRepository;

    public ListService(ListRepository listRepository, UserRepository userRepository)
    {
        _listRepository = listRepository;
        _userRepository = userRepository;
    }

    public async Task<ShoppingListResponse?> GetListAsync(string userId)
    {
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
        if (list == null) return null;

        return MapToResponse(list);
    }

    public async Task<ShoppingItemResponse?> AddItemAsync(string userId, AddItemRequest request, string addedBy)
    {
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
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
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
        if (list == null) return null;

        var item = list.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return null;

        var now = DateTime.UtcNow;
        item.Description = request.Description.Trim();
        item.Quantity = request.Quantity;
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        item.PhotoUrl = request.PhotoUrl;
        item.UpdatedAt = now;

        list.UpdatedAt = now;
        await _listRepository.ReplaceAsync(list);

        return MapItemToResponse(item);
    }

    public async Task<bool> DeleteItemAsync(string userId, string itemId)
    {
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
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
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
        if (list == null) return null;

        list.Items.Add(itemDoc);
        list.UpdatedAt = itemDoc.UpdatedAt;
        await _listRepository.ReplaceAsync(list);

        return MapItemToResponse(itemDoc);
    }

    public async Task<List<ShoppingItemResponse>?> AddItemsDirectAsync(string userId, List<ShoppingItemDocument> items)
    {
        var list = await _listRepository.GetByOwnerUserIdAsync(userId);
        if (list == null) return null;

        list.Items.AddRange(items);
        list.UpdatedAt = DateTime.UtcNow;
        await _listRepository.ReplaceAsync(list);

        return items.Select(MapItemToResponse).ToList();
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
            PhotoUrl = item.PhotoUrl,
            Confidence = item.Confidence,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }
}
