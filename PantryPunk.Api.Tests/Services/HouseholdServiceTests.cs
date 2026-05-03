using Moq;
using PantryPunk.Api.Models.Documents;
using PantryPunk.Api.Repositories;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Tests.Services;

public class HouseholdServiceTests
{
    private readonly Mock<ShareRepository> _shareRepo = new();
    private readonly Mock<UserService> _userService;
    private readonly HouseholdService _sut;

    public HouseholdServiceTests()
    {
        _userService = new Mock<UserService>(
            new Mock<UserRepository>().Object,
            new Mock<ListRepository>().Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<UserService>>().Object);

        _sut = new HouseholdService(_userService.Object, _shareRepo.Object);
    }

    [Fact]
    public async Task GetMembersAsync_NoShareCodes_ReturnsOwnerOnly()
    {
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike", IsSubscriber = true
        });
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true))
            .ReturnsAsync(new List<ShareCodeDocument>());

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.NotNull(result);
        Assert.Single(result!.Members);
        Assert.Equal("Mike", result.Members[0].DisplayName);
        Assert.True(result.Members[0].IsOwner);
        Assert.Null(result.Members[0].ShareId);
        Assert.Null(result.Members[0].ConfirmedAt);
    }

    [Fact]
    public async Task GetMembersAsync_OwnerMissing_ReturnsNull()
    {
        var ownerId = "auth0|missing";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync((UserDocument?)null);

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.Null(result);
        _shareRepo.Verify(r => r.GetByOwnerUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetMembersAsync_OwnerFirstAndConfirmedGuestsFollow()
    {
        var ownerId = "auth0|owner";
        var now = DateTime.UtcNow;
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike", IsSubscriber = true
        });

        // Repo returns docs ordered by createdAt ASC; we feed them in that order.
        var docs = new List<ShareCodeDocument>
        {
            new()
            {
                Id = "sc-1", Code = "ABC123", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Sarah", Confirmed = true, ConfirmedAt = now.AddHours(-2),
                ExpiresAt = now.AddHours(22), CreatedAt = now.AddHours(-3)
            },
            new()
            {
                Id = "sc-2", Code = "DEF456", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Jamie", Confirmed = true, ConfirmedAt = now.AddHours(-1),
                ExpiresAt = now.AddHours(23), CreatedAt = now.AddHours(-2)
            }
        };
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true)).ReturnsAsync(docs);

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Members.Count);

        Assert.True(result.Members[0].IsOwner);
        Assert.Equal("Mike", result.Members[0].DisplayName);

        Assert.False(result.Members[1].IsOwner);
        Assert.Equal("Sarah", result.Members[1].DisplayName);
        Assert.Equal("sc-1", result.Members[1].ShareId);

        Assert.False(result.Members[2].IsOwner);
        Assert.Equal("Jamie", result.Members[2].DisplayName);
        Assert.Equal("sc-2", result.Members[2].ShareId);
    }

    [Fact]
    public async Task GetMembersAsync_FiltersOutUnconfirmedCodes()
    {
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike", IsSubscriber = true
        });

        var docs = new List<ShareCodeDocument>
        {
            new()
            {
                Id = "sc-confirmed", Code = "ABC123", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Sarah", Confirmed = true, ConfirmedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(20), CreatedAt = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                Id = "sc-pending", Code = "DEF456", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = null, Confirmed = false,
                ExpiresAt = DateTime.UtcNow.AddHours(20), CreatedAt = DateTime.UtcNow
            }
        };
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true)).ReturnsAsync(docs);

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Members.Count);
        Assert.True(result.Members[0].IsOwner);
        Assert.Equal("Sarah", result.Members[1].DisplayName);
        Assert.DoesNotContain(result.Members, m => m.ShareId == "sc-pending");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetMembersAsync_OwnerDisplayNameMissing_FallsBackToOwner(string? displayName)
    {
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = displayName!
        });
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true))
            .ReturnsAsync(new List<ShareCodeDocument>());

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.NotNull(result);
        Assert.Equal("Owner", result!.Members[0].DisplayName);
        Assert.True(result.Members[0].IsOwner);
    }

    [Fact]
    public async Task GetMembersAsync_ShareCodeGuest_MarksMatchingMemberAsCurrentUser()
    {
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike"
        });

        var docs = new List<ShareCodeDocument>
        {
            new()
            {
                Id = "sc-1", Code = "ABC123", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Sarah", Confirmed = true, ConfirmedAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new()
            {
                Id = "sc-2", Code = "DEF456", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Jamie", Confirmed = true, ConfirmedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24), CreatedAt = DateTime.UtcNow.AddHours(-1)
            }
        };
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true)).ReturnsAsync(docs);

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: "sc-2");

        Assert.NotNull(result);
        Assert.False(result!.Members[0].IsCurrentUser); // owner
        Assert.False(result.Members[1].IsCurrentUser);  // Sarah (sc-1)
        Assert.True(result.Members[2].IsCurrentUser);   // Jamie (sc-2) — the caller
    }

    [Fact]
    public async Task GetMembersAsync_JwtCaller_NoMemberMarkedCurrentUser()
    {
        // JWT caller has no ShareId claim — authenticatedShareId is null. No member should be flagged.
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike"
        });
        var docs = new List<ShareCodeDocument>
        {
            new()
            {
                Id = "sc-1", Code = "ABC123", OwnerUserId = ownerId, ListId = "list-1",
                RecipientName = "Sarah", Confirmed = true, ConfirmedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(23), CreatedAt = DateTime.UtcNow.AddHours(-1)
            }
        };
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true)).ReturnsAsync(docs);

        var result = await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        Assert.NotNull(result);
        Assert.All(result!.Members, m => Assert.False(m.IsCurrentUser));
    }

    [Fact]
    public async Task GetMembersAsync_RepoExcludesRevoked()
    {
        // ShareRepository.GetByOwnerUserIdAsync(ownerId, excludeRevoked: true) is what the service calls.
        // Verify the service passes the default (true) so revoked codes are filtered at the data layer.
        var ownerId = "auth0|owner";
        _userService.Setup(s => s.GetDocumentAsync(ownerId)).ReturnsAsync(new UserDocument
        {
            Id = ownerId, UserId = ownerId, DisplayName = "Mike"
        });
        _shareRepo.Setup(r => r.GetByOwnerUserIdAsync(ownerId, true))
            .ReturnsAsync(new List<ShareCodeDocument>());

        await _sut.GetMembersAsync(ownerId, authenticatedShareId: null);

        _shareRepo.Verify(r => r.GetByOwnerUserIdAsync(ownerId, true), Times.Once);
        _shareRepo.Verify(r => r.GetByOwnerUserIdAsync(ownerId, false), Times.Never);
    }
}
