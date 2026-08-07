using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Indexes a folder (recursively by default). Streams one
/// <see cref="IndexingProgress"/> event per file as it completes — never
/// buffers the whole run before returning, so both local and MCP-remote
/// consumers see progress in real time.
/// </summary>
public interface IIndexingUseCase
{
    /// <param name="clearFirst">
    /// Wipes both the <c>documents</c> and <c>images</c> collections before
    /// indexing — recovery path for stale/duplicate data (e.g. points left
    /// over from before chunk ids were made deterministic). Off by default:
    /// this is destructive and shouldn't be a silent/implicit part of a
    /// routine re-index.
    /// </param>
    IAsyncEnumerable<IndexingProgress> IndexAsync(
        string path,
        bool recursive = true,
        bool clearFirst = false,
        CancellationToken cancellationToken = default);
}
