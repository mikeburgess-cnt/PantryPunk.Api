using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class UpdateProfileRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string DisplayName { get; set; } = null!;

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }
}
