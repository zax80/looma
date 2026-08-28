using System.Text.Json.Nodes;

namespace Looma.CLI.Setup;

/// <summary>
/// The starting point <see cref="SetupWizard"/> uses when config.json
/// doesn't exist yet — every field here matches the documented default in
/// docs/config-reference.md, so a brand-new install ends up identical to
/// what a person would get copying the reference doc by hand. The wizard
/// only ever overwrites <c>DomainName</c>, <c>Deployment</c>, <c>Models</c>,
/// and <c>Redis.Enabled</c> on top of this — see <see cref="ConfigMerger"/>.
/// </summary>
public static class DefaultConfigTemplate
{
    public static JsonObject Build() => (JsonObject)JsonNode.Parse(Json)!;

    private const string Json = """
        {
          "DomainName": "Looma",
          "Version": "2.0.0",
          "Deployment": {
            "Mode": "Standalone"
          },
          "Models": {
            "BaseModel": {
              "Provider": "Ollama",
              "Endpoint": "http://localhost:11434",
              "Model": "qwen3:8b",
              "ContextSize": 8192,
              "SupportsThinking": false
            },
            "EmbeddingModel": {
              "Provider": "Ollama",
              "Endpoint": "http://localhost:11434",
              "Model": "nomic-embed-text",
              "Dimensions": 768
            },
            "VisionModel": {
              "Provider": "Ollama",
              "Endpoint": "http://localhost:11434",
              "Model": "qwen2.5vl:7b"
            },
            "ImageEmbeddingModel": {
              "Provider": "Local.OnnxClip",
              "ModelPath": "./models/clip-vit-b32.onnx",
              "DownloadUrl": "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx",
              "Dimensions": 512
            },
            "SpeechToTextModel": {
              "Provider": "Local.Whisper",
              "ModelPath": "./models/whisper-small.bin",
              "DownloadUrl": "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
              "Language": "auto"
            }
          },
          "VectorStore": {
            "Provider": "Qdrant",
            "Endpoint": "http://localhost:6333",
            "ApiKey": null,
            "Collections": {
              "Documents": "documents",
              "Images": "images"
            }
          },
          "Redis": {
            "Enabled": false,
            "Endpoint": "localhost:6379",
            "Password": null
          },
          "WebSearch": {
            "Endpoint": "https://html.duckduckgo.com",
            "TimeoutSeconds": 10
          },
          "Mcp": {
            "Enabled": false,
            "Auth": {
              "Mode": "ApiKey",
              "ApiKeyEnvVar": "LOOMA_MCP_API_KEY"
            },
            "AllowedHosts": ["localhost", "127.0.0.1", "::1"],
            "Tls": {
              "Enabled": true,
              "CertificatePath": null,
              "CertificatePasswordEnvVar": null
            }
          },
          "RAG": {
            "Sources": [
              {
                "Type": "FileSystem",
                "Path": "./data",
                "FileTypes": [".pdf", ".docx", ".xlsx", ".txt", ".md", ".csv", ".png", ".jpg", ".jpeg", ".wav", ".mp3"],
                "Recursive": true
              }
            ],
            "ChunkSize": 800,
            "ChunkOverlap": 100,
            "TopK": 5,
            "MinRelevanceScore": 0.55,
            "MaxAnswerTokens": null,
            "AnswerTemperature": 0.1,
            "EnableQueryReformulation": true,
            "EnableAdaptiveThreshold": true,
            "AdaptiveFloorScore": 0.40,
            "AdaptiveThresholdMargin": 0.15,
            "EnableWebSearch": true,
            "WebSearchMaxResults": 3,
            "WebSearchGenerationTimeoutSeconds": 180,
            "EnableHybridSearch": true,
            "HybridRelevanceRatio": 0.5
          },
          "AnswerCache": {
            "Enabled": true,
            "FilePath": "./.looma/answer-cache.json",
            "CollectionName": "answer_cache",
            "SemanticSimilarityThreshold": 0.97,
            "WebCitationMaxAgeHours": 24,
            "MaxExactEntries": 500
          },
          "ChatHistory": {
            "SessionsFilePath": "./.looma/chat-sessions.json",
            "SavedAnswersFilePath": "./.looma/saved-answers.json"
          },
          "Security": {
            "AllowedInferenceHosts": ["localhost", "127.0.0.1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"],
            "BlockNonLocalEndpoints": true
          },
          "Agents": {
            "Enabled": false,
            "Definitions": []
          }
        }
        """;
}
