namespace Looma.Core.Entities;

/// <summary>
/// One result from <see cref="Looma.Core.Abstractions.IWebSearchProvider"/> —
/// deliberately minimal (title/url/snippet only, no ranking score or
/// engine attribution) since the only consumer,
/// <c>Looma.Application.Internal.WebSearchFallback</c>, maps these straight
/// into <c>DocumentChunk</c>s for the existing citation pipeline rather
/// than exposing provider-specific detail up the stack.
/// </summary>
public sealed record WebSearchResult
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Snippet { get; init; }
}
