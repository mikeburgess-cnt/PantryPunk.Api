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
        _listRepo.Setup(r => r.GetByOwnerUserIdAsync(userId)).ReturnsAsync(list);
        _shareRepo.Setup(r => r.ActiveCodeExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _shareRepo.Setup(r => r.CreateAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var request = new GenerateShareCodeRequest { RecipientName = "  Natalie  " };
        var result = await _sut.GenerateCodeAsync(userId, request);

        Assert.Equal("Natalie", result.RecipientName); // trimmed
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
            _sut.GenerateCodeAsync("auth0|free", new GenerateShareCodeRequest { RecipientName = "X" }));
    }

    [Fact]
    public async Task GenerateCodeAsync_AllCodesCollide_ThrowsConflict()
    {
        var userId = "auth0|sub1";
        _userService.Setup(s => s.RequireSubscriberAsync(userId)).Returns(Task.CompletedTask);
        _listRepo.Setup(r => r.GetByOwnerUserIdAsync(userId))
            .ReturnsAsync(new ShoppingListDocument { Id = "list-1", ListId = "list-1", OwnerUserId = userId });
        _shareRepo.Setup(r => r.ActiveCodeExistsAsync(It.IsAny<string>())).ReturnsAsync(true); // always collides

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.GenerateCodeAsync(userId, new GenerateShareCodeRequest { RecipientName = "X" }));
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

        var (success, recipientName, error) = await _sut.ConfirmCodeAsync(
            new ConfirmShareCodeRequest { Code = "abc123" }); // lowercased input

        Assert.True(success);
        Assert.Equal("Natalie", recipientName);
        Assert.Null(error);
        Assert.True(doc.Confirmed);
        Assert.NotNull(doc.ConfirmedAt);
    }

    [Fact]
    public async Task ConfirmCodeAsync_CodeNotFound_ReturnsFalse()
    {
        _shareRepo.Setup(r => r.GetByCodeAsync("XXXXXX")).ReturnsAsync((ShareCodeDocument?)null);

        var (success, _, error) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "XXXXXX" });

        Assert.False(success);
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

        var (success, _, error) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "ABC123" });

        Assert.False(success);
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

        var (success, _, error) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "ABC123" });

        Assert.False(success);
    }

    [Fact]
    public async Task ConfirmCodeAsync_AlreadyConfirmed_DoesNotReconfirm()
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

        var (success, recipientName, _) = await _sut.ConfirmCodeAsync(new ConfirmShareCodeRequest { Code = "ABC123" });

        Assert.True(success);
        Assert.Equal("Natalie", recipientName);
        Assert.Equal(confirmedAt, doc.ConfirmedAt); // not overwritten
        _shareRepo.Verify(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()), Times.Never);
    }

    [Fact]
    public async Task GetShareCodesAsync_ReturnsMappedList()
    {
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
    public async Task RevokeAsync_SetsRevokedAt()
    {
        var doc = new ShareCodeDocument
        {
            Id = "sc-1", Code = "ABC123", OwnerUserId = "auth0|owner", ListId = "list-1",
            RecipientName = "Natalie", CreatedAt = DateTime.UtcNow
        };
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("sc-1", "auth0|owner")).ReturnsAsync(doc);
        _shareRepo.Setup(r => r.ReplaceAsync(It.IsAny<ShareCodeDocument>()))
            .ReturnsAsync((ShareCodeDocument d) => d);

        var result = await _sut.RevokeAsync("sc-1", "auth0|owner");

        Assert.True(result);
        Assert.NotNull(doc.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_NotFound_ReturnsFalse()
    {
        _shareRepo.Setup(r => r.GetByIdAndOwnerAsync("missing", "auth0|owner"))
            .ReturnsAsync((ShareCodeDocument?)null);

        var result = await _sut.RevokeAsync("missing", "auth0|owner");

        Assert.False(result);
    }
}
