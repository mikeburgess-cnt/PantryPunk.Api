using System.Text.Json.Serialization;

namespace PantryPunk.Api.Models.Documents;

public class AppConfigDocument
{
    public const string DocumentId = "app-config";

    [JsonPropertyName("id")]
    public string Id { get; set; } = DocumentId;

    [JsonPropertyName("settings")]
    public Dictionary<string, string?> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
