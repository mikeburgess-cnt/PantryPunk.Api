using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class UpdateProfileRequest
{
    [Required]
    public string DisplayName { get; set; } = null!;

    public string? Email { get; set; }
}
