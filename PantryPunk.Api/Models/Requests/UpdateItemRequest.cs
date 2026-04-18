using System.ComponentModel.DataAnnotations;

namespace PantryPunk.Api.Models.Requests;

public class UpdateItemRequest
{
    [Required]
    public string Description { get; set; } = null!;

    public int? Quantity { get; set; }

    public string? Notes { get; set; }

    public string? PhotoUrl { get; set; }
}
