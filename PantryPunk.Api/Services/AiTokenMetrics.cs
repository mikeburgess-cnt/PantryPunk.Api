using Microsoft.ApplicationInsights;

namespace PantryPunk.Api.Services;

public class AiTokenMetrics
{
    private const string AnthropicMetricName = "ai.tokens.image.anthropic";
    private const string OpenAiMetricName = "ai.tokens.image.openai";

    private readonly TelemetryClient _telemetry;

    public AiTokenMetrics(TelemetryClient telemetry) => _telemetry = telemetry;

    public void TrackAnthropic(string model, int inputTokens, int outputTokens,
                               int cacheCreationTokens, int cacheReadTokens)
        => Track(AnthropicMetricName, model, inputTokens, outputTokens,
                 cacheCreationTokens, cacheReadTokens);

    public void TrackOpenAi(string model, int inputTokens, int outputTokens)
        => Track(OpenAiMetricName, model, inputTokens, outputTokens, 0, 0);

    private void Track(string metricName, string model, int input, int output,
                       int cacheCreation, int cacheRead)
    {
        var metric = _telemetry.GetMetric(metricName, "tokenType", "model");
        if (input > 0) metric.TrackValue(input, "input", model);
        if (output > 0) metric.TrackValue(output, "output", model);
        if (cacheCreation > 0) metric.TrackValue(cacheCreation, "cache_creation", model);
        if (cacheRead > 0) metric.TrackValue(cacheRead, "cache_read", model);
    }
}
