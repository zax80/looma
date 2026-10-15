using Looma.CLI.Setup;
using Xunit;

namespace Looma.CLI.Tests;

public class DefaultConfigTemplateTests
{
    [Fact]
    public void Build_ProducesAParsableObjectWithEveryTopLevelSection()
    {
        var root = DefaultConfigTemplate.Build();

        Assert.Equal("Looma", root["DomainName"]!.GetValue<string>());
        foreach (var section in new[]
                 {
                     "Deployment", "Models", "VectorStore", "Redis", "WebSearch",
                     "Mcp", "RAG", "AnswerCache", "ChatHistory", "Security", "Agents"
                 })
        {
            Assert.NotNull(root[section]);
        }
    }

    [Fact]
    public void Build_ReturnsAFreshObjectEachCall()
    {
        var first = DefaultConfigTemplate.Build();
        first["DomainName"] = "Mutated";

        var second = DefaultConfigTemplate.Build();

        Assert.Equal("Looma", second["DomainName"]!.GetValue<string>());
    }
}
