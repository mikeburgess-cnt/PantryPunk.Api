using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class GenerateShareCodeRequest
{
    [Required]
    public string RecipientName { get; set; } = null!;
}
