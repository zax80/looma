using System.Text.Json;
using System.Text.Json.Serialization;

namespace Looma.MCP.Server;

/// <summary>
/// JSON options for the structured payloads carried inside MCP progress
/// notification <c>Message</c> fields. <see cref="Tools.IndexTool"/>,
/// <see cref="Tools.SearchTool"/>, and <see cref="Tools.AnswerTool"/> all
/// serialize the actual <c>Looma.Core.Entities</c> record (<c>IndexingProgress</c>,
/// <c>VectorSearchResult</c>, <c>AnswerToken</c>) directly as the wire format —
/// no separate DTO layer, since Looma.MCP.Client references the same
/// <c>Looma.Core</c> types and can deserialize straight back into them.
///
/// This is the one thing that makes real per-item streaming possible for a
/// remote MCP-client consumer: the final <c>CallToolResult</c> text is still a
/// human-readable summary for Inspector/chat-style clients, but a structured
/// client reconstructs the exact stream by parsing each progress notification.
///
/// Looma.MCP.Client duplicates this small options object (rather than the two
/// projects sharing a third one just for this) — keep them in sync if this
/// changes.
/// </summary>
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
