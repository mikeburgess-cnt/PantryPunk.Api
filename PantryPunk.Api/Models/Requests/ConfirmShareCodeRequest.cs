using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class ConfirmShareCodeRequest
{
    [Required, RegularExpression("^[A-Z0-9]{6}$", ErrorMessage = "Code must be 6 uppercase alphanumeric characters.")]
    public string Code { get; set; } = null!;

    [Required, StringLength(64, MinimumLength = 1)]
    public string RecipientName { get; set; } = null!;
}
