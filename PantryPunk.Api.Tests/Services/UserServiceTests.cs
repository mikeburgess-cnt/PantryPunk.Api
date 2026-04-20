using Microsoft.Extensions.Logging;
using Moq;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<UserRepository> _userRepo = new();
    private readonly Mock<ListRepository> _listRepo = new();
    private readonly Mock<ILogger<UserService>> _logger = new();
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _sut = new UserService(_userRepo.Object, _listRepo.Object, _logger.Object);
    }

    [Fact]
    public async Task UpsertProfileAsync_NewUser_CreatesUserAndShoppingList()
    {
        var userId = "auth0|new123";
        var request = new UpdateProfileRequest { DisplayName = "  Mike  ", Email = "mike@test.com" };

        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync((UserDocument?)null);
        _userRepo.Setup(r => r.UpsertAsync(It.IsAny<UserDocument>()))
            .ReturnsAsync((UserDocument d) => d);
        _listRepo.Setup(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()))
            .ReturnsAsync((ShoppingListDocument d) => d);

        var result = await _sut.UpsertProfileAsync(userId, request);

        Assert.Equal(userId, result.Id);
        Assert.Equal("Mike", result.DisplayName); // trimmed
        Assert.False(result.IsSubscriber);
        _listRepo.Verify(r => r.CreateAsync(It.Is<ShoppingListDocument>(d => d.OwnerUserId == userId)), Times.Once);
    }

    [Fact]
    public async Task UpsertProfileAsync_ExistingUser_UpdatesWithoutCreatingList()
    {
        var userId = "auth0|existing";
        var existing = new UserDocument
        {
            Id = userId, UserId = userId, DisplayName = "Old", CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var request = new UpdateProfileRequest { DisplayName = "Updated" };

        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(existing);
        _userRepo.Setup(r => r.UpsertAsync(It.IsAny<UserDocument>()))
            .ReturnsAsync((UserDocument d) => d);

        var result = await _sut.UpsertProfileAsync(userId, request);

        Assert.Equal("Updated", result.DisplayName);
        _listRepo.Verify(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()), Times.Never);
    }

    [Fact]
    public async Task UpsertProfileAsync_ListCreationFails_DoesNotThrow()
    {
        var userId = "auth0|faillist";
        var request = new UpdateProfileRequest { DisplayName = "Mike" };

        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync((UserDocument?)null);
        _userRepo.Setup(r => r.UpsertAsync(It.IsAny<UserDocument>()))
            .ReturnsAsync((UserDocument d) => d);
        _listRepo.Setup(r => r.CreateAsync(It.IsAny<ShoppingListDocument>()))
            .ThrowsAsync(new Exception("Cosmos error"));

        var result = await _sut.UpsertProfileAsync(userId, request);

        Assert.Equal("Mike", result.DisplayName); // should not throw
    }

    [Fact]
    public async Task GetProfileAsync_UserExists_ReturnsProfile()
    {
        var userId = "auth0|abc";
        var doc = new UserDocument { Id = userId, UserId = userId, DisplayName = "Mike", IsSubscriber = true };
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(doc);

        var result = await _sut.GetProfileAsync(userId);

        Assert.NotNull(result);
        Assert.Equal("Mike", result!.DisplayName);
        Assert.True(result.IsSubscriber);
    }

    [Fact]
    public async Task GetProfileAsync_UserNotFound_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((UserDocument?)null);

        var result = await _sut.GetProfileAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_SetsSubscriberAndTimestamp()
    {
        var userId = "auth0|sub";
        var doc = new UserDocument { Id = userId, UserId = userId, DisplayName = "Mike" };
        _userRepo.Setup(r => r.GetByIdAsync(userId)).ReturnsAsync(doc);
        _userRepo.Setup(r => r.UpsertAsync(It.IsAny<UserDocument>()))
            .ReturnsAsync((UserDocument d) => d);

        var result = await _sut.UpdateSubscriptionAsync(userId, new UpdateSubscriptionRequest { IsSubscriber = true });

        Assert.NotNull(result);
        Assert.True(result!.IsSubscriber);
        _userRepo.Verify(r => r.UpsertAsync(It.Is<UserDocument>(d =>
            d.IsSubscriber && d.SubscribedAt != null)), Times.Once);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_UserNotFound_ReturnsNull()
    {
        _userRepo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((UserDocument?)null);

        var result = await _sut.UpdateSubscriptionAsync("missing", new UpdateSubscriptionRequest { IsSubscriber = true });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_AlreadySubscribed_DoesNotResetSubscribedAt()
    {
        var originalDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var doc = new UserDocument
        {
            Id = "auth0|x", UserId = "auth0|x", DisplayName = "Mike",
            IsSubscriber = true, SubscribedAt = originalDate
        };
        _userRepo.Setup(r => r.GetByIdAsync("auth0|x")).ReturnsAsync(doc);
        _userRepo.Setup(r => r.UpsertAsync(It.IsAny<UserDocument>()))
            .ReturnsAsync((UserDocument d) => d);

        await _sut.UpdateSubscriptionAsync("auth0|x", new UpdateSubscriptionRequest { IsSubscriber = true });

        _userRepo.Verify(r => r.UpsertAsync(It.Is<UserDocument>(d =>
            d.SubscribedAt == originalDate)), Times.Once);
    }

    [Fact]
    public async Task RequireSubscriberAsync_Subscriber_DoesNotThrow()
    {
        var doc = new UserDocument { Id = "auth0|sub", UserId = "auth0|sub", IsSubscriber = true };
        _userRepo.Setup(r => r.GetByIdAsync("auth0|sub")).ReturnsAsync(doc);

        await _sut.RequireSubscriberAsync("auth0|sub"); // should not throw
    }

    [Fact]
    public async Task RequireSubscriberAsync_NotSubscriber_ThrowsForbidden()
    {
        var doc = new UserDocument { Id = "auth0|free", UserId = "auth0|free", IsSubscriber = false };
        _userRepo.Setup(r => r.GetByIdAsync("auth0|free")).ReturnsAsync(doc);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RequireSubscriberAsync("auth0|free"));
    }

    [Fact]
    public async Task RequireSubscriberAsync_UserNotFound_ThrowsForbidden()
    {
        _userRepo.Setup(r => r.GetByIdAsync("missing")).ReturnsAsync((UserDocument?)null);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.RequireSubscriberAsync("missing"));
    }
}
