using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class UpdateShareCodeRecipientNameRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string RecipientName { get; set; } = null!;
}
