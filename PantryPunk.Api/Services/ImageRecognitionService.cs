using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PantryPunk.Api.Services;

public class ImageRecognitionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageRecognitionService> _logger;

    private const string SystemPrompt = """
        You are a grocery item recognition assistant.
        When given an image of a grocery product, respond ONLY with a JSON object
        containing the following fields:
        - description: the full product name including brand, variant, and size (string)
        - brand: the brand name only (string or null if not identifiable)
        - quantity: suggested quantity to add to a shopping list (integer or null if unclear)
        - confidence: your confidence in the identification ("high", "medium", or "low")

        Confidence guidelines:
        - "high": brand, product name, and size are all clearly visible and identified
        - "medium": product type is clear but brand or size is uncertain or partially obscured
        - "low": image is unclear, partially visible, or the product cannot be reliably identified

        Example response:
        {"description":"Flora ProActiv Buttery Spread 750g","brand":"Flora","quantity":null,"confidence":"high"}

        Do not include any other text, explanation, or markdown. JSON only.
        """;

    public ImageRecognitionService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ImageRecognitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ImageRecognitionResult?> RecogniseAsync(byte[] imageBytes)
    {
        var model = _configuration["PantryPunk:Claude:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = _configuration.GetValue("PantryPunk:Claude:MaxTokensImage", 256);
        var apiKey = _configuration["Claude:ApiKey"]
            ?? throw new InvalidOperationException("Claude:ApiKey is not configured.");

        var base64Image = Convert.ToBase64String(imageBytes);

        var requestBody = new
        {
            model,
            max_tokens = maxTokens,
            system = SystemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = "image/jpeg",
                                data = base64Image
                            }
                        }
                    }
                }
            }
        };

        var client = _httpClientFactory.CreateClient("Claude");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var claudeResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var textContent = claudeResponse.GetProperty("content")[0].GetProperty("text").GetString();
        if (string.IsNullOrEmpty(textContent))
        {
            _logger.LogWarning("Claude returned empty text content for image recognition");
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<ImageRecognitionResult>(textContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null || string.IsNullOrWhiteSpace(result.Description))
                return null;

            // Validate confidence value
            if (result.Confidence is not ("high" or "medium" or "low"))
                result.Confidence = "low";

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Claude image recognition response: {Response}", textContent);
            return null;
        }
    }
}

public class ImageRecognitionResult
{
    public string Description { get; set; } = null!;
    public string? Brand { get; set; }
    public int? Quantity { get; set; }
    public string Confidence { get; set; } = "low";
}
