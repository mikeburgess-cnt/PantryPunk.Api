using Microsoft.Extensions.Configuration;
using Moq;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Models.Requests;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Tests.Services;

public class ShareServiceTests
{
    private readonly Mock<ShareRepository> _shareRepo = new();
    private readonly Mock<ListRepository> _listRepo = new();
    private readonly Mock<UserService> _userService;
    private readonly IConfiguration _configuration;
    private readonly ShareService _sut;

    public ShareServiceTests()
    {
        _userService = new Mock<UserService>(
            new Mock<UserRepository>().Object,
            new Mock<ListRepository>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<UserService>>().Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PantryPunk:ShareCode:ExpiryHours"] = "24"
            })
            .Build();

        _sut = new ShareService(_shareRepo.Object, _listRepo.Object, _userService.Object, _configuration);
    }

    [Fact]
    public async Task GenerateCodeAsync_Success_ReturnsShareCode()
    {
        var userId = "auth0|sub1";
        var list = new ShoppingListDocument
        {
            Id = "list-1", ListId = "list-1", OwnerUserId = userId
        };

        _userService.Setup(s => s.RequireSubscriberAsync(userId)).Returns(Task.CompletedTask);
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync(userId)).ReturnsAsync(list);
        _shareRepo.Setup(r => r.ActiveCodeExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _shareRepo.Setup(r => r.CreateAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var request = new GenerateShareCodeRequest();
        var result = await _sut.GenerateCodeAsync(userId, request);

        Assert.Null(result.RecipientName); // set later at confirm-code time
        Assert.Equal(6, result.Code.Length);
        Assert.False(result.Confirmed);
        Assert.NotEmpty(result.ShareId);
    }

    [Fact]
    public async Task GenerateCodeAsync_NotSubscriber_ThrowsForbidden()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|free"))
            .ThrowsAsync(new ForbiddenException("Sharing requires an active subscription."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GenerateCodeAsync("auth0|free", new GenerateShareCodeRequest()));
    }

    [Fact]
    public async Task GenerateCodeAsync_AllCodesCollide_ThrowsConflict()
    {
        var userId = "auth0|sub1";
        _userService.Setup(s => s.RequireSubscriberAsync(userId)).Returns(Task.CompletedTask);
        _listRepo.Setup(r => r.GetActiveByOwnerUserIdAsync(userId))
            .ReturnsAsync(new ShoppingListDocument { Id = "list-1", ListId = "list-1", OwnerUserId = userId });
        _shareRepo.Setup(r => r.ActiveCodeExistsAsync(It.IsAny<string>())).ReturnsAsync(true); // always collides

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.GenerateCodeAsync(userId, new GenerateShareCodeRequest()));
    }

    [Fact]
    public async Task ConfirmCodeAsync_ValidCode_ConfirmsAndReturnsRecipient()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
            Confirmed = false, ExpiresAt = DateTime.UtcNow.AddHours(12),
            OwnerUserId = "auth0|owner", ListId = "list-1", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByCodeAsync("ABC123")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var (response, error) = await _sut.ConfirmCodeAsync(
            new ConfirmShareCodeRequest { Code = "abc123", RecipientName = "Natalie" }); // lowercased input

        Assert.NotNull(response);
        Assert.Equal("sc-1", response!.ShareId);
        Assert.Equal("ABC123", response.Code);
        Assert.Equal("Natalie", response.RecipientName);
        Assert.True(response.Confirmed);
        Assert.NotNull(response.ConfirmedAt);
        Assert.Null(error);
        Assert.True(doc.Confirmed);
        Assert.NotNull(doc.ConfirmedAt);
    }

    [Fact]
    public async Task ConfirmCodeAsync_CodeNotFound_ReturnsFalse()
    {
        _shareRepo.Setup(r => r.GetByCodeAsync("XXXXXX")).ReturnsAsync((ShareCodeDocument?)null);

        var (response, error) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "XXXXXX" });

        Assert.Null(response);
        Assert.Equal("Invalid, expired, or revoked code", error);
    }

    [Fact]
    public async Task ConfirmCodeAsync_RevokedCode_ReturnsFalse()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
            RevokedAt = DateTime.UtcNow.AddHours(-1),
            OwnerUserId = "auth0|owner", ListId = "list-1", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByCodeAsync("ABC123")).ReturnsAsync(doc);

        var (response, error) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "ABC123" });

        Assert.Null(response);
        Assert.Contains("revoked", error!);
    }

    [Fact]
    public async Task ConfirmCodeAsync_ExpiredUnconfirmedCode_ReturnsFalse()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
            Confirmed = false, ExpiresAt = DateTime.UtcNow.AddHours(-1),
            OwnerUserId = "auth0|owner", ListId = "list-1", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByCodeAsync("ABC123")).ReturnsAsync(doc);

        var (response, _) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "ABC123" });

        Assert.Null(response);
    }

    [Fact]
    public async Task ConfirmCodeAsync_AlreadyConfirmed_NewName_OverwritesAndPreservesConfirmedAt()
    {
        var confirmedAt = DateTime.UtcNow.AddHours(-2);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
            Confirmed = true, ConfirmedAt = confirmedAt,
            ExpiresAt = DateTime.UtcNow.AddHours(-1), // expired but already confirmed
            OwnerUserId = "auth0|owner", ListId = "list-1", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByCodeAsync("ABC123")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var (response, _) = await _sut.ConfirmCodeAsync(
            new ConfirmShareCodeRequest { Code = "ABC123", RecipientName = "Nat" });

        Assert.NotNull(response);
        Assert.Equal("Nat", response!.RecipientName);
        Assert.Equal("sc-1", response.ShareId);
        Assert.Equal("Nat", doc.RecipientName);
        Assert.Equal(confirmedAt, doc.ConfirmedAt); // not overwritten on re-confirm
        _shareRepo.Verify(r => r.ReplaceAsync(It.Is<ShareCodeDocument>(d => d.RecipientName == "Nat")), Times.Once);
    }

    [Fact]
    public async Task ConfirmCodeAsync_AlreadyConfirmed_SameName_DoesNotWrite()
    {
        var confirmedAt = DateTime.UtcNow.AddHours(-2);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
            Confirmed = true, ConfirmedAt = confirmedAt,
            ExpiresAt = DateTime.UtcNow.AddHours(-1), // expired but already confirmed
            OwnerUserId = "auth0|owner", ListId = "list-1", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByCodeAsync("ABC123")).ReturnsAsync(doc);

        var (response, _) = await _sut.ConfirmCodeAsync(
            new ConfirmShareCodeRequest { Code = "ABC123", RecipientName = "  Natalie  " });

        Assert.NotNull(response);
        Assert.Equal("Natalie", response!.RecipientName);
        Assert.Equal("Natalie", doc.RecipientName);
        Assert.Equal(confirmedAt, doc.ConfirmedAt);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task GetShareCodesAsync_ReturnsMappedList()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        var docs = new List<ShareCodeDocument>
        {
            new() { Id = "sc-1", Code = "ABC123", RecipientName = "Natalie",
                     Confirmed = true, ConfirmedAt = DateTime.UtcNow,
                     ExpiresAt = DateTime.UtcNow.AddHours(24), CreatedAt = DateTime.UtcNow,
                     OwnerUserId = "auth0|owner", ListId = "list-1" }
        };
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync("auth0|owner", true)).ReturnsAsync(docs);

        var result = await _sut.GetShareCodesAsync("auth0|owner");

        Assert.Single(result);
        Assert.Equal("Natalie", result[0].RecipientName);
        Assert.NotNull(result[0].ConfirmedAt); // list response includes ConfirmedAt
        Assert.NotEqual(default, result[0].CreatedAt); // list response includes CreatedAt
    }

    [Fact]
    public async Task GetShareCodesAsync_NotSubscriber_ThrowsForbidden()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|free"))
            .ThrowsAsync(new ForbiddenException("Sharing requires an active subscription."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetShareCodesAsync("auth0|free"));
        _shareRepo.Verify(r => r.GetByOwnerUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_SubscriberOwner_DifferentName_UpdatesAndWrites()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", Confirmed = true, ConfirmedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "sc-1", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null,
            new UpdateShareCodeRecipientNameRequest { RecipientName = "Nat" });

        Assert.NotNull(response);
        Assert.Equal("Nat", response!.RecipientName);
        Assert.Equal("sc-1", response.ShareId);
        Assert.Null(error);
        Assert.Null(statusCode);
        Assert.Equal("Nat", doc.RecipientName);
        _shareRepo.Verify(r => r.ReplaceAsync(It.Is<ShareCodeDocument>(d => d.RecipientName == "Nat")), Times.Once);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_SameNameAfterTrim_DoesNotWrite()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", Confirmed = true, ConfirmedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "sc-1", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null,
            new UpdateShareCodeRecipientNameRequest { RecipientName = "  Natalie  " });

        Assert.NotNull(response);
        Assert.Equal("Natalie", response!.RecipientName);
        Assert.Equal("Natalie", doc.RecipientName);
        Assert.Null(error);
        Assert.Null(statusCode);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_NotSubscriber_ThrowsForbidden()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|free"))
            .ThrowsAsync(new ForbiddenException("Sharing requires an active subscription."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateRecipientNameAsync("sc-1", "auth0|free",
                isShareCodeUser: false, authenticatedShareId: null,
                new UpdateShareCodeRecipientNameRequest { RecipientName = "Alice" }));

        _shareRepo.Verify(r => r.GetByIdAndOwnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_SubscriberDoesNotOwnShareCode_ReturnsNotFound()
    {
        // Caller is a valid subscriber but the shareId belongs to a different owner.
        // GetByIdAndOwnerAsync filters on (id AND ownerUserId), so the repo returns null —
        // we deliberately return 404 (not 403) to avoid leaking the existence of other subscribers' codes.
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|attacker")).Returns(Task.CompletedTask);
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-victim", "auth0|attacker"))
            .ReturnsAsync((ShareCodeDocument?)null);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "sc-victim", "auth0|attacker", isShareCodeUser: false, authenticatedShareId: null,
            new UpdateShareCodeRecipientNameRequest { RecipientName = "Hacked" });

        Assert.Null(response);
        Assert.Equal(404, statusCode);
        Assert.Equal("Share code not found.", error);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_ShareIdNotFound_ReturnsNotFound()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("missing", "auth0|owner"))
            .ReturnsAsync((ShareCodeDocument?)null);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "missing", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null,
            new UpdateShareCodeRecipientNameRequest { RecipientName = "Natalie" });

        Assert.Null(response);
        Assert.Equal(404, statusCode);
        Assert.Equal("Share code not found.", error);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_Unconfirmed_ReturnsConflict()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = null, Confirmed = false,
            ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "sc-1", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null,
            new UpdateShareCodeRecipientNameRequest { RecipientName = "Natalie" });

        Assert.Null(response);
        Assert.Equal(409, statusCode);
        Assert.Equal("Share code has not been confirmed by the recipient yet.", error);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_GuestSelfRename_UpdatesAndSkipsSubscriberCheck()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", Confirmed = true, ConfirmedAt = DateTime.UtcNow.AddHours(-1),
            ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-2)
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var (response, error, statusCode) = await _sut.UpdateRecipientNameAsync(
            "sc-1", "auth0|owner", isShareCodeUser: true, authenticatedShareId: "sc-1",
            new UpdateShareCodeRecipientNameRequest { RecipientName = "Nat" });

        Assert.NotNull(response);
        Assert.Equal("Nat", response!.RecipientName);
        Assert.Null(error);
        Assert.Null(statusCode);
        _shareRepo.Verify(r => r.ReplaceAsync(It.Is<ShareCodeDocument>(d => d.RecipientName == "Nat")), Times.Once);
        _userService.Verify(s => s.RequireSubscriberAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecipientNameAsync_GuestMismatchedShareId_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateRecipientNameAsync(
                "sc-other", "auth0|owner", isShareCodeUser: true, authenticatedShareId: "sc-self",
                new UpdateShareCodeRecipientNameRequest { RecipientName = "Hacker" }));

        _shareRepo.Verify(r => r.GetByIdAndOwnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
        _userService.Verify(s => s.RequireSubscriberAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_SubscriberOwner_SetsRevokedAt()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var result = await _sut.RevokeAsync("sc-1", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null);

        Assert.True(result);
        Assert.NotNull(doc.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_NotSubscriberJwt_ThrowsForbidden()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|free"))
            .ThrowsAsync(new ForbiddenException("Sharing requires an active subscription."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RevokeAsync("sc-1", "auth0|free", isShareCodeUser: false, authenticatedShareId: null));
        _shareRepo.Verify(r => r.GetByIdAndOwnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_NotFound_ReturnsFalse()
    {
        _userService.Setup(s => s.RequireSubscriberAsync("auth0|owner")).Returns(Task.CompletedTask);
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("missing", "auth0|owner"))
            .ReturnsAsync((ShareCodeDocument?)null);

        var result = await _sut.RevokeAsync("missing", "auth0|owner", isShareCodeUser: false, authenticatedShareId: null);

        Assert.False(result);
    }

    [Fact]
    public async Task RevokeAsync_ShareCodeUserRevokesOwnCode_Succeeds()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var result = await _sut.RevokeAsync("sc-1", "auth0|owner", isShareCodeUser: true, authenticatedShareId: "sc-1");

        Assert.True(result);
        Assert.NotNull(doc.RevokedAt);
        _userService.Verify(s => s.RequireSubscriberAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_ShareCodeUserRevokesOtherCode_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RevokeAsync("sc-other", "auth0|owner", isShareCodeUser: true, authenticatedShareId: "sc-1"));

        _shareRepo.Verify(r => r.GetByIdAndOwnerAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _userService.Verify(s => s.RequireSubscriberAsync(It.IsAny<string>()), Times.Never);
    }
}
