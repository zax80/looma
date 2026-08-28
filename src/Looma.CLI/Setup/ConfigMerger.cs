using System.Text.Json.Nodes;

namespace Looma.CLI.Setup;

/// <summary>
/// Applies the wizard's answers on top of an existing (or brand-new, from
/// <see cref="DefaultConfigTemplate"/>) config.json <see cref="JsonObject"/>
/// — deliberately a targeted merge, not a full overwrite: re-running
/// <c>looma setup</c> against a config someone has already hand-tuned (RAG
/// thresholds, chunk size, TLS settings, etc.) must leave everything it
/// wasn't asked about untouched. Only <c>DomainName</c>, <c>Deployment</c>,
/// <c>Models.*.Model</c>/<c>SupportsThinking</c>/<c>Language</c>, and
/// <c>Redis.Enabled</c> are ever written here. Pure/no I/O — easy to unit
/// test without touching a real file.
/// </summary>
public static class ConfigMerger
{
    public static void Apply(JsonObject root, SetupAnswers answers, ModelRecommendation recommendation)
    {
        root["DomainName"] = answers.DomainName;

        var deployment = GetOrCreateObject(root, "Deployment");
        deployment["Mode"] = answers.DeploymentMode;
        if (answers.DeploymentMode == "McpClient")
        {
            deployment["McpServerEndpoint"] = answers.McpServerEndpoint ?? "https://localhost:3001";
        }

        var models = GetOrCreateObject(root, "Models");

        var baseModel = GetOrCreateObject(models, "BaseModel");
        baseModel["Model"] = recommendation.BaseModel;
        baseModel["SupportsThinking"] = recommendation.SupportsThinking;
        baseModel.TryAdd("Provider", "Ollama");
        baseModel.TryAdd("Endpoint", "http://localhost:11434");
        baseModel.TryAdd("ContextSize", 8192);

        var visionModel = GetOrCreateObject(models, "VisionModel");
        visionModel["Model"] = recommendation.VisionModel;
        visionModel.TryAdd("Provider", "Ollama");
        visionModel.TryAdd("Endpoint", "http://localhost:11434");

        var embeddingModel = GetOrCreateObject(models, "EmbeddingModel");
        embeddingModel["Model"] = recommendation.EmbeddingModel;
        embeddingModel.TryAdd("Provider", "Ollama");
        embeddingModel.TryAdd("Endpoint", "http://localhost:11434");
        embeddingModel.TryAdd("Dimensions", 768);

        var speechToTextModel = GetOrCreateObject(models, "SpeechToTextModel");
        speechToTextModel["Language"] = recommendation.WhisperLanguage;
        speechToTextModel.TryAdd("Provider", "Local.Whisper");
        speechToTextModel.TryAdd("ModelPath", "./models/whisper-small.bin");
        speechToTextModel.TryAdd(
            "DownloadUrl", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin");

        var redis = GetOrCreateObject(root, "Redis");
        redis["Enabled"] = answers.HorizontalScale;
        redis.TryAdd("Endpoint", "localhost:6379");
        redis.TryAdd("Password", null);
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[key] = created;
        return created;
    }
}
