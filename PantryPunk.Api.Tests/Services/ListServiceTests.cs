using Moq;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Tests.Services;

public class ListServiceTests
{
    private readonly Mock<ListRepository> _listRepo = new();
    private readonly Mock<UserRepository> _userRepo = new();
    private readonly ListService _sut;

    public ListServiceTests()
    {
        _sut = new ListService(_listRepo.Object, _userRepo.Object);
    }

    private static ShoppingListDocument CreateList(string userId = "auth0|abc", params ShoppingItemDocument[] items)
    {
        return new ShoppingListDocument
        {
            Id = "list-1", ListId = "list-1", OwnerUserId = userId,
            Items = items.ToList(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    private static ShoppingItemDocument CreateItem(string id = "item-1", string description = "Milk")
    {
        return new ShoppingItemDocument
        {
            Id = id, Description = description, Quantity = 1, AddedBy = "Mike",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetListAsync_ListExists_ReturnsMappedResponse()
    {
        var list = CreateList("auth0|abc", CreateItem());
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);

        var result = await _sut.GetListAsync("auth0|abc");

        Assert.NotNull(result);
        Assert.Equal("list-1", result!.ListId);
        Assert.Single(result.Items);
        Assert.Equal("Milk", result.Items[0].Description);
    }

    [Fact]
    public async Task GetListAsync_ListNotFound_ReturnsNull()
    {
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("missing")).ReturnsAsync((ShoppingListDocument?)null);

        var result = await _sut.GetListAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task AddItemAsync_AddsItemToList()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var request = new AddItemRequest { Description = "  Bananas  ", Quantity = 3, Notes = " ripe " };
        var result = await _sut.AddItemAsync("auth0|abc", request, "Mike");

        Assert.NotNull(result);
        Assert.Equal("Bananas", result!.Description); // trimmed
        Assert.Equal(3, result.Quantity);
        Assert.Equal("ripe", result.Notes); // trimmed
        Assert.Equal("Mike", result.AddedBy);
        Assert.Single(list.Items);
    }

    [Fact]
    public async Task AddItemAsync_EmptyNotes_SetsToNull()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var request = new AddItemRequest { Description = "Bread", Notes = "   " };
        var result = await _sut.AddItemAsync("auth0|abc", request, "Mike");

        Assert.Null(result!.Notes);
    }

    [Fact]
    public async Task AddItemAsync_ListNotFound_ReturnsNull()
    {
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("missing")).ReturnsAsync((ShoppingListDocument?)null);

        var result = await _sut.AddItemAsync("missing", new AddItemRequest { Description = "X" }, "Mike");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateItemAsync_UpdatesExistingItem()
    {
        var item = CreateItem("item-1", "Old");
        var list = CreateList("auth0|abc", item);
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var request = new UpdateItemRequest { Description = "New", Quantity = 5, Notes = "fresh" };
        var result = await _sut.UpdateItemAsync("auth0|abc", "item-1", request);

        Assert.NotNull(result);
        Assert.Equal("New", result!.Description);
        Assert.Equal(5, result.Quantity);
        Assert.Equal("fresh", result.Notes);
    }

    [Fact]
    public async Task UpdateItemAsync_ItemNotFound_ReturnsNull()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);

        var result = await _sut.UpdateItemAsync("auth0|abc", "nonexistent", new UpdateItemRequest { Description = "X" });

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteItemAsync_RemovesItemFromList()
    {
        var item = CreateItem("item-1");
        var list = CreateList("auth0|abc", item);
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var result = await _sut.DeleteItemAsync("auth0|abc", "item-1");

        Assert.True(result);
        Assert.Empty(list.Items);
        _listRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()), Times.Once);
    }

    [Fact]
    public async Task DeleteItemAsync_ItemNotFound_ReturnsFalse()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);

        var result = await _sut.DeleteItemAsync("auth0|abc", "nonexistent");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteItemAsync_ListNotFound_ReturnsFalse()
    {
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("missing")).ReturnsAsync((ShoppingListDocument?)null);

        var result = await _sut.DeleteItemAsync("missing", "item-1");

        Assert.False(result);
    }

    [Fact]
    public async Task AddItemDirectAsync_AppendsPrebuiltItem()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var itemDoc = CreateItem("direct-1", "Photo Item");
        var result = await _sut.AddItemDirectAsync("auth0|abc", itemDoc);

        Assert.NotNull(result);
        Assert.Equal("Photo Item", result!.Description);
        Assert.Single(list.Items);
    }

    [Fact]
    public async Task AddItemsDirectAsync_AppendsBatchItems()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var items = new List<ShoppingItemDocument> { CreateItem("v1", "Milk"), CreateItem("v2", "Eggs") };
        var result = await _sut.AddItemsDirectAsync("auth0|abc", items);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public async Task CompleteAsync_NoActiveList_ReturnsNull()
    {
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("missing")).ReturnsAsync((ShoppingListDocument?)null);

        var result = await _sut.CompleteAsync("missing");

        Assert.Null(result);
        _listRepo.Verify(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()), Times.Never);
        _listRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_EmptyList_ThrowsEmptyListException()
    {
        var list = CreateList();
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(list);

        await Assert.ThrowsAsync<EmptyListException>(() => _sut.CompleteAsync("auth0|abc"));
        _listRepo.Verify(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()), Times.Never);
        _listRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAsync_WithItems_CreatesNewActiveAndCompletesOld()
    {
        var oldList = CreateList("auth0|abc", CreateItem());
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync("auth0|abc")).ReturnsAsync(oldList);
        _listRepo.Setup(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);
        _listRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        ShoppingListDocument? created = null;
        _listRepo.Setup(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()))
            .Callback<ShoppingListDocument>(d => created = d)
            .ReturnsAsync((ShoppingListDocument d) => d);

        var result = await _sut.CompleteAsync("auth0|abc");

        Assert.NotNull(result);
        Assert.NotNull(created);
        Assert.Equal(created!.ListId, result!.ListId);
        Assert.NotEqual(oldList.ListId, result.ListId);
        Assert.Empty(result.Items);
        Assert.Equal(ShoppingListStatus.Active, created.Status);
        Assert.Equal(ShoppingListStatus.Completed, oldList.Status);
        Assert.NotNull(oldList.CompletedAt);
        _listRepo.Verify(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()), Times.Once);
        _listRepo.Verify(r => r.ReplaceAsync(oldList), Times.Once);
    }

    [Fact]
    public async Task ResolveAddedByAsync_WithRecipientName_ReturnsRecipientName()
    {
        var result = await _sut.ResolveAddedByAsync("auth0|abc", "Natalie");

        Assert.Equal("Natalie", result);
        _userRepo.Verify(r => r.GetByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAddedByAsync_WithoutRecipientName_ReturnsDisplayName()
    {
        var doc = new UserDocument { Id = "auth0|abc", UserId = "auth0|abc", DisplayName = "Mike" };
        _userRepo.Setup(r => r.GetByIdAsync("auth0|abc")).ReturnsAsync(doc);

        var result = await _sut.ResolveAddedByAsync("auth0|abc", null);

        Assert.Equal("Mike", result);
    }

    [Fact]
    public async Task ResolveAddedByAsync_UserNotFound_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((UserDocument?)null);

        var result = await _sut.ResolveAddedByAsync("missing", null);

        Assert.Null(result);
    }
}
