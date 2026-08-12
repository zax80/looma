using Looma.Core.Exceptions;
using Looma.MCP.Server.Tools;
using ModelContextProtocol;
using Xunit;

namespace Looma.MCP.Server.Tests;

public class ToolErrorTranslationTests
{
    [Fact]
    public void Translate_VectorStoreUnavailableException_PreservesMessageAndInnerException()
    {
        var inner = new HttpRequestException("Connection refused");
        var source = new VectorStoreUnavailableException("Can't reach Qdrant to search 'documents'.", inner);

        var translated = ToolErrorTranslation.Translate(source);

        // McpException.Message crossing the wire verbatim is the whole
        // point (see McpException's own SDK docs) — this is what actually
        // fixes the generic "An error occurred invoking 'x'" the client
        // used to see instead.
        Assert.IsType<McpException>(translated);
        Assert.Equal(source.Message, translated.Message);
        Assert.Same(source, translated.InnerException);
    }
}
