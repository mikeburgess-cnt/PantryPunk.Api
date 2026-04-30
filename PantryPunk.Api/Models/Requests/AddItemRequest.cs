using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class AddItemRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string Description { get; set; } = null!;

    [Range(1, 1000)]
    public int? Quantity { get; set; }

    [StringLength(1024)]
    public string? Notes { get; set; }
}
