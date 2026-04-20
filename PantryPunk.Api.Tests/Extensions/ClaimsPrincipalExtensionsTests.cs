using System.Security.Claims;
using PantryPunk.Api.Extensions;

namespace PantryPunk.Api.Tests.Extensions;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetUserId_WithNameIdentifierClaim_ReturnsValue()
    {
        var principal = CreatePrincipal(ClaimTypes.NameIdentifier, "auth0|abc123");

        var result = principal.GetUserId();

        Assert.Equal("auth0|abc123", result);
    }

    [Fact]
    public void GetUserId_WithoutClaim_ThrowsUnauthorized()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Throws<UnauthorizedAccessException>(() => principal.GetUserId());
    }

    [Fact]
    public void GetRecipientName_WithClaim_ReturnsValue()
    {
        var principal = CreatePrincipal("RecipientName", "Natalie");

        var result = principal.GetRecipientName();

        Assert.Equal("Natalie", result);
    }

    [Fact]
    public void GetRecipientName_WithoutClaim_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = principal.GetRecipientName();

        Assert.Null(result);
    }

    [Fact]
    public void IsShareCodeUser_WithRecipientName_ReturnsTrue()
    {
        var principal = CreatePrincipal("RecipientName", "Natalie");

        Assert.True(principal.IsShareCodeUser());
    }

    [Fact]
    public void IsShareCodeUser_WithoutRecipientName_ReturnsFalse()
    {
        var principal = CreatePrincipal(ClaimTypes.NameIdentifier, "auth0|abc");

        Assert.False(principal.IsShareCodeUser());
    }

    [Fact]
    public void GetShareId_WithClaim_ReturnsValue()
    {
        var principal = CreatePrincipal("ShareId", "sc-1");

        var result = principal.GetShareId();

        Assert.Equal("sc-1", result);
    }

    [Fact]
    public void GetShareId_WithoutClaim_ReturnsNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = principal.GetShareId();

        Assert.Null(result);
    }

    private static ClaimsPrincipal CreatePrincipal(string claimType, string claimValue)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(claimType, claimValue) }, "Test");
        return new ClaimsPrincipal(identity);
    }
}
