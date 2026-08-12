namespace Looma.Core.Entities;

/// <summary>The originating media type of an ingested source.</summary>
public enum MediaType
{
    Text,
    Audio,
    Image,

    /// <summary>
    /// A web search result, never indexed/stored — see
    /// <see cref="Looma.Core.Abstractions.IWebSearchProvider"/>. Reuses the
    /// existing <c>DocumentChunk</c>/citation pipeline end-to-end (session
    /// persistence, MCP wire format, export, UI rendering) instead of a
    /// parallel type, the same way Image/Audio already do — only the
    /// citation display format needs a distinct case (a URL, not a line or
    /// timestamp range; see <c>ChunkMetadata.SourcePath</c>).
    /// </summary>
    Web
}
