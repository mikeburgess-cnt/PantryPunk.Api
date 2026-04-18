using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class AddItemRequest
{
    [Required]
    public string Description { get; set; } = null!;

    public int? Quantity { get; set; }

    public string? Notes { get; set; }
}
