using System.Text;
using System.Text.Json;

namespace PantryPunk.Api.Services;

public class VoiceRecognitionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VoiceRecognitionService> _logger;

    private const string SystemPrompt = """
        You are a shopping list voice assistant.
        The user has spoken a message describing items they want to add to their shopping list.
        Extract all shopping items mentioned and respond ONLY with a JSON object:
        {
          "items": [
            { "description": "item name", "quantity": number_or_null }
          ]
        }

        Rules:
        - Normalise item names (e.g. "2 litres of full cream milk" → description: "Full cream milk", quantity: 2)
        - If no quantity is mentioned, set quantity to null
        - Include all distinct items mentioned
        - Do not include any other text or markdown. JSON only.
        """;

    public VoiceRecognitionService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<VoiceRecognitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> TranscribeAsync(Stream audioStream)
    {
        var speechKey = _configuration["AzureSpeech:Key"]
            ?? throw new InvalidOperationException("AzureSpeech:Key is not configured.");
        var speechRegion = _configuration["AzureSpeech:Region"]
            ?? throw new InvalidOperationException("AzureSpeech:Region is not configured.");

        var client = _httpClientFactory.CreateClient("AzureSpeech");
        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", speechKey);

        using var content = new StreamContent(audioStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mp4");

        var url = $"https://{speechRegion}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1?language=en-AU";
        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Azure Speech transcription failed with status {StatusCode}", response.StatusCode);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var status = result.GetProperty("RecognitionStatus").GetString();
        if (status != "Success")
        {
            _logger.LogWarning("Azure Speech transcription status: {Status}", status);
            return null;
        }

        return result.GetProperty("DisplayText").GetString();
    }

    public async Task<List<VoiceItemResult>?> ExtractItemsAsync(string transcription)
    {
        var model = _configuration["PantryPunk:Claude:Model"] ?? "claude-sonnet-4-6";
        var maxTokens = _configuration.GetValue("PantryPunk:Claude:MaxTokensVoice", 512);
        var apiKey = _configuration["Claude:ApiKey"]
            ?? throw new InvalidOperationException("Claude:ApiKey is not configured.");

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
                    content = transcription
                }
            }
        };

        var client = _httpClientFactory.CreateClient("Claude");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("https://api.anthropic.com/v1/messages", httpContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var claudeResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var textContent = claudeResponse.GetProperty("content")[0].GetProperty("text").GetString();
        if (string.IsNullOrEmpty(textContent))
        {
            _logger.LogWarning("Claude returned empty text for voice item extraction");
            return null;
        }

        try
        {
            var result = JsonSerializer.Deserialize<VoiceExtractionResult>(textContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result?.Items == null || result.Items.Count == 0)
                return null;

            return result.Items;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Claude voice extraction response: {Response}", textContent);
            return null;
        }
    }
}

public class VoiceExtractionResult
{
    public List<VoiceItemResult> Items { get; set; } = new();
}

public class VoiceItemResult
{
    public string Description { get; set; } = null!;
    public int? Quantity { get; set; }
}
