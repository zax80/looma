using Looma.MCP.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Looma.MCP.Client.Tests;

public sealed class RemoteStreamHelperTests
{
    [Fact]
    public void ExtractText_SingleTextContentBlock_ReturnsItsText()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "hello" }]
        };

        Assert.Equal("hello", RemoteStreamHelper.ExtractText(result));
    }

    [Fact]
    public void ExtractText_FirstOfMultipleTextBlocks_ReturnsFirst()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "first" },
                new TextContentBlock { Text = "second" }
            ]
        };

        Assert.Equal("first", RemoteStreamHelper.ExtractText(result));
    }

    [Fact]
    public void ExtractText_NoContent_ReturnsNull()
    {
        var result = new CallToolResult { Content = [] };

        Assert.Null(RemoteStreamHelper.ExtractText(result));
    }
}
