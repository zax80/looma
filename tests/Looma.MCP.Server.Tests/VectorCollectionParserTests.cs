using Looma.Core.Abstractions;
using Looma.MCP.Server.Tools;
using ModelContextProtocol;
using Xunit;

namespace Looma.MCP.Server.Tests;

public sealed class VectorCollectionParserTests
{
    [Theory]
    [InlineData("documents", VectorCollection.Documents)]
    [InlineData("Documents", VectorCollection.Documents)]
    [InlineData("DOCUMENTS", VectorCollection.Documents)]
    [InlineData(" documents ", VectorCollection.Documents)]
    [InlineData("images", VectorCollection.Images)]
    [InlineData("Images", VectorCollection.Images)]
    public void Parse_KnownCollectionNames_ReturnsExpectedEnumValue(string input, VectorCollection expected)
    {
        Assert.Equal(expected, VectorCollectionParser.Parse(input));
    }

    [Theory]
    [InlineData("document")]
    [InlineData("image")]
    [InlineData("")]
    [InlineData("answer_cache")]
    public void Parse_UnknownCollectionName_ThrowsMcpExceptionWithMessage(string input)
    {
        var ex = Assert.Throws<McpException>(() => VectorCollectionParser.Parse(input));
        Assert.Contains(input, ex.Message);
    }
}
