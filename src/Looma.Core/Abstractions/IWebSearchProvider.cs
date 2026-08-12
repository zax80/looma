using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>
/// A single external web search — the fallback source
/// <c>Looma.Application.Internal.WebSearchFallback</c> reaches for when
/// local retrieval (the <c>documents</c> vector collection) finds nothing
/// usable for a given query, gated behind <c>RagOptions.EnableWebSearch</c>.
/// Never used for indexing — a web result is fetched fresh per turn, folded
/// into that turn's context, and discarded, never written to the vector
/// store.
///
/// Deliberately NOT a streaming <c>IAsyncEnumerable</c> like
/// <see cref="IVectorStore.SearchAsync"/> — a metasearch call returns one
/// batch of already-ranked results, not a long-running operation, so there's
/// nothing to stream.
///
/// A single implementation is expected at a time (see
/// <c>Looma.Infrastructure.WebSearch.SearXng</c>'s
/// <c>SearXngWebSearchProvider</c>), same "one implementation, no
/// hand-rolled abstraction gymnastics" spirit as <see cref="IVectorStore"/>
/// — this interface exists so <c>Looma.Application</c> never references an
/// Infrastructure.* project directly (CLAUDE.md rule 1), not to support
/// swappable providers day one.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Best-effort: implementations should catch their own connectivity/
    /// parsing failures and return an empty list rather than throwing,
    /// the same "must never break answering" philosophy as
    /// <see cref="IAnswerCache"/> — this is a fallback for when local
    /// retrieval already came up empty, not the primary retrieval path, so
    /// a broken or unreachable search backend should degrade to "no web
    /// results" rather than fail the whole turn. <see cref="OperationCanceledException"/>
    /// should still propagate.
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
