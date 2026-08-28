using Looma.CLI.Setup;
using Xunit;

namespace Looma.CLI.Tests;

public class ModelRecommendationCatalogTests
{
    [Theory]
    [InlineData("Bulgaria")]
    [InlineData("bulgaria")]
    [InlineData("Sofia, Bulgaria")]
    public void Recommend_KnownLocation_UsesRegionTunedModelAndLanguage(string location)
    {
        var recommendation = ModelRecommendationCatalog.Recommend(domain: "General support", location);

        Assert.Equal("hf.co/INSAIT-Institute/BgGPT-Gemma-3-12B-IT-GGUF:Q4_K_M", recommendation.BaseModel);
        Assert.Equal("bg", recommendation.WhisperLanguage);
        Assert.False(recommendation.SupportsThinking);
    }

    [Fact]
    public void Recommend_UnknownLocation_FallsBackToAutoDetectAndGenericModel()
    {
        var recommendation = ModelRecommendationCatalog.Recommend(domain: "General", location: "Nowhereland");

        Assert.Equal("auto", recommendation.WhisperLanguage);
        Assert.Equal("qwen3:8b", recommendation.BaseModel);
    }

    [Theory]
    [InlineData("Legal review")]
    [InlineData("Medical records")]
    [InlineData("Compliance audit")]
    public void Recommend_CarefulDomain_PicksLargerModelWithThinkingEnabled(string domain)
    {
        var recommendation = ModelRecommendationCatalog.Recommend(domain, location: "Nowhereland");

        Assert.Equal("qwen3:14b", recommendation.BaseModel);
        Assert.True(recommendation.SupportsThinking);
    }

    [Theory]
    [InlineData("Customer support")]
    [InlineData("FAQ helpdesk")]
    public void Recommend_FastDomain_PicksSmallerModelWithoutThinking(string domain)
    {
        var recommendation = ModelRecommendationCatalog.Recommend(domain, location: "Nowhereland");

        Assert.Equal("qwen3:4b", recommendation.BaseModel);
        Assert.False(recommendation.SupportsThinking);
    }

    [Fact]
    public void Recommend_LocationOverride_TakesPrecedenceOverDomainTierModel()
    {
        // A "careful" domain (would otherwise pick qwen3:14b) combined with
        // a location that has its own region-tuned model — the location
        // override wins, since it's the more specific, actually-verified
        // recommendation for that language.
        var recommendation = ModelRecommendationCatalog.Recommend(domain: "Legal review", location: "Bulgaria");

        Assert.Equal("hf.co/INSAIT-Institute/BgGPT-Gemma-3-12B-IT-GGUF:Q4_K_M", recommendation.BaseModel);
        Assert.False(recommendation.SupportsThinking);
    }

    [Fact]
    public void Recommend_VisionAndEmbeddingModels_AreAlwaysTheGenericDefaults()
    {
        var recommendation = ModelRecommendationCatalog.Recommend(domain: "Legal", location: "Bulgaria");

        Assert.Equal("qwen2.5vl:7b", recommendation.VisionModel);
        Assert.Equal("nomic-embed-text", recommendation.EmbeddingModel);
    }
}
