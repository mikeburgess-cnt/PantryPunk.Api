using System.Text.Json.Serialization;

namespace PantryPunk.Api.Models.Responses;

public class ShoppingItemResponse
{
    public string Id { get; set; } = null!;
    public string Description { get; set; } = null!;

    [JsonIgnore]
    public string? Size { get; set; } = null!;

    [JsonIgnore]
    public string? Brand { get; set; } = null!;

    [JsonIgnore]
    public string? KnownAs { get; set; } = null!;
    public int? Quantity { get; set; }
    public string AddedBy { get; set; } = null!;
    public string AddedByMethod { get; set; } = null!;
    public string? Notes { get; set; }
    public bool HasPhoto { get; set; }
    public string? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string DisplayDescription
    {
        get
        {
            var method = AddedByMethod?.ToUpperInvariant();

            if (method == "PHOTO" && !string.IsNullOrWhiteSpace(KnownAs))
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Brand) &&
                    KnownAs.IndexOf(Brand, StringComparison.OrdinalIgnoreCase) < 0)
                    parts.Add(Brand);

                parts.Add(KnownAs);

                if (!string.IsNullOrWhiteSpace(Size) &&
                    KnownAs.IndexOf(Size, StringComparison.OrdinalIgnoreCase) < 0)
                    parts.Add(Size);

                return string.Join(' ', parts).Trim();
            }

            return Description ?? string.Empty;
        }
    }
}
