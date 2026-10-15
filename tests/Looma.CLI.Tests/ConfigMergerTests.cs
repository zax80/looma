using System.Text.Json.Nodes;
using Looma.CLI.Setup;
using Xunit;

namespace Looma.CLI.Tests;

public class ConfigMergerTests
{
    private static readonly ModelRecommendation Recommendation = new(
        BaseModel: "qwen3:8b",
        VisionModel: "qwen2.5vl:7b",
        EmbeddingModel: "nomic-embed-text",
        WhisperLanguage: "auto",
        SupportsThinking: false,
        Note: "test");

    [Fact]
    public void Apply_ToFreshTemplate_SetsAllWizardFields()
    {
        var root = DefaultConfigTemplate.Build();
        var answers = new SetupAnswers("Acme", "Bulgaria", "Standalone", McpServerEndpoint: null, HorizontalScale: true);

        ConfigMerger.Apply(root, answers, Recommendation);

        Assert.Equal("Acme", root["DomainName"]!.GetValue<string>());
        Assert.Equal("Standalone", root["Deployment"]!["Mode"]!.GetValue<string>());
        Assert.Equal("qwen3:8b", root["Models"]!["BaseModel"]!["Model"]!.GetValue<string>());
        Assert.Equal("qwen2.5vl:7b", root["Models"]!["VisionModel"]!["Model"]!.GetValue<string>());
        Assert.Equal("nomic-embed-text", root["Models"]!["EmbeddingModel"]!["Model"]!.GetValue<string>());
        Assert.Equal("auto", root["Models"]!["SpeechToTextModel"]!["Language"]!.GetValue<string>());
        Assert.True(root["Redis"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_McpClientMode_WritesServerEndpoint()
    {
        var root = DefaultConfigTemplate.Build();
        var answers = new SetupAnswers("Acme", "", "McpClient", "https://mcp.internal:3001", HorizontalScale: false);

        ConfigMerger.Apply(root, answers, Recommendation);

        Assert.Equal("McpClient", root["Deployment"]!["Mode"]!.GetValue<string>());
        Assert.Equal("https://mcp.internal:3001", root["Deployment"]!["McpServerEndpoint"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_PreservesUntouchedFieldsOnAnExistingHandTunedConfig()
    {
        var root = DefaultConfigTemplate.Build();

        // Simulate a person who already hand-tuned some unrelated settings.
        root["RAG"]!["ChunkSize"] = 1200;
        root["RAG"]!["MinRelevanceScore"] = 0.75;
        root["Models"]!["BaseModel"]!["ContextSize"] = 32768;
        root["Models"]!["BaseModel"]!["TimeoutSeconds"] = 900;

        var answers = new SetupAnswers("Acme", "Bulgaria", "Standalone", McpServerEndpoint: null, HorizontalScale: false);
        ConfigMerger.Apply(root, answers, Recommendation);

        // Wizard fields did get updated...
        Assert.Equal("Acme", root["DomainName"]!.GetValue<string>());
        Assert.Equal("qwen3:8b", root["Models"]!["BaseModel"]!["Model"]!.GetValue<string>());

        // ...but hand-tuned, non-wizard fields were left completely alone.
        Assert.Equal(1200, root["RAG"]!["ChunkSize"]!.GetValue<int>());
        Assert.Equal(0.75, root["RAG"]!["MinRelevanceScore"]!.GetValue<double>());
        Assert.Equal(32768, root["Models"]!["BaseModel"]!["ContextSize"]!.GetValue<int>());
        Assert.Equal(900, root["Models"]!["BaseModel"]!["TimeoutSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void Apply_ToMinimalObject_CreatesMissingSectionsWithoutThrowing()
    {
        var root = new JsonObject();
        var answers = new SetupAnswers("Acme", "Bulgaria", "Standalone", McpServerEndpoint: null, HorizontalScale: true);

        var exception = Record.Exception(() => ConfigMerger.Apply(root, answers, Recommendation));

        Assert.Null(exception);
        Assert.Equal("Acme", root["DomainName"]!.GetValue<string>());
        Assert.True(root["Redis"]!["Enabled"]!.GetValue<bool>());
    }
}
