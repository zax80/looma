using System.Text.Json.Serialization;

namespace Looma.Infrastructure.Llm;

/// <summary>One line of the newline-delimited JSON stream Ollama's <c>/api/pull</c> returns.</summary>
public sealed class OllamaPullEvent
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("total")]
    public long? Total { get; init; }

    [JsonPropertyName("completed")]
    public long? Completed { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);

    public bool IsError => !string.IsNullOrEmpty(Error);

    /// <summary>Human-readable one-line progress description, e.g. "downloading (42%)" or "pulling manifest". Pure formatting — easy to unit test.</summary>
    public string Describe(string modelName)
    {
        if (IsError)
        {
            return $"{modelName}: {Error}";
        }

        if (Total is > 0 && Completed is { } completed)
        {
            var percent = (int)(completed * 100 / Total.Value);
            return $"{modelName}: {Status} ({percent}%)";
        }

        return $"{modelName}: {Status}";
    }
}
