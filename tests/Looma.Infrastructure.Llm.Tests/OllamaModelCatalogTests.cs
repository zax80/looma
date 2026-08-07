using Looma.Infrastructure.Llm;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

public sealed class OllamaModelCatalogTests
{
    [Fact]
    public void FindMissing_ReturnsModelsNotInInstalledList()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["qwen3:8b", "nomic-embed-text"],
            installedModels: ["qwen3:8b"]);

        Assert.Equal(["nomic-embed-text"], missing);
    }

    [Fact]
    public void FindMissing_MatchIsCaseInsensitive()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["Qwen3:8B"],
            installedModels: ["qwen3:8b"]);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_IgnoresBlankRequiredEntries()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["qwen3:8b", "", "   "],
            installedModels: []);

        Assert.Equal(["qwen3:8b"], missing);
    }

    [Fact]
    public void FindMissing_DeduplicatesRequiredEntries()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["qwen3:8b", "qwen3:8b"],
            installedModels: []);

        Assert.Equal(["qwen3:8b"], missing);
    }

    [Fact]
    public void FindMissing_EverythingInstalled_ReturnsEmpty()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["qwen3:8b", "nomic-embed-text"],
            installedModels: ["qwen3:8b", "nomic-embed-text", "some-other-model"]);

        Assert.Empty(missing);
    }

    /// <summary>
    /// Regression test for the real bug hit in practice: config.json specifies
    /// a tag-less model name, but Ollama's /api/tags always reports it back
    /// with an implicit ":latest" — without normalizing, this looked
    /// "missing" and got re-pulled (a real registry round trip) on every
    /// single CLI invocation, cache hit or not.
    /// </summary>
    [Fact]
    public void FindMissing_TagLessRequiredMatchesImplicitLatestInstalled()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["nomic-embed-text"],
            installedModels: ["qwen3:8b", "nomic-embed-text:latest"]);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissing_TagLessRequiredDoesNotMatchDifferentExplicitTag()
    {
        var missing = OllamaModelCatalog.FindMissing(
            requiredModels: ["nomic-embed-text"],
            installedModels: ["nomic-embed-text:v1.5"]);

        Assert.Equal(["nomic-embed-text"], missing);
    }
}
