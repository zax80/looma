using System.Text.Json.Serialization;

namespace Looma.Infrastructure.Llm;

internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaTagModel>? Models { get; init; }
}

internal sealed class OllamaTagModel
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
