using Looma.Core.Abstractions;
using ModelContextProtocol;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Shared by <see cref="SearchTool"/> and <see cref="CountTool"/>. Tools
/// take <c>collection</c> as a plain string rather than binding
/// <see cref="VectorCollection"/> directly, so the exposed JSON schema and
/// error message are fully controlled here rather than left to however the
/// SDK happens to serialize a .NET enum.
///
/// Throws <see cref="McpException"/>, not <see cref="McpProtocolException"/>
/// — per the SDK's own docs, protocol exceptions are for transport/protocol
/// failures, not tool-input validation. A plain <see cref="McpException"/>
/// becomes a normal <c>IsError</c> tool result with this message preserved,
/// which is exactly what an LLM caller needs to see in order to self-correct
/// and retry with a valid value.
/// </summary>
public static class VectorCollectionParser
{
    public static VectorCollection Parse(string collection) => collection.Trim().ToLowerInvariant() switch
    {
        "documents" => VectorCollection.Documents,
        "images" => VectorCollection.Images,
        _ => throw new McpException($"Unknown collection '{collection}'. Expected \"documents\" or \"images\".")
    };
}
