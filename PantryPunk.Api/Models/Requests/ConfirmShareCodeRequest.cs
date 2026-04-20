using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class ConfirmShareCodeRequest
{
    [Required]
    public string Code { get; set; } = null!;

    [Required]
    public string RecipientName { get; set; } = null!;
}
