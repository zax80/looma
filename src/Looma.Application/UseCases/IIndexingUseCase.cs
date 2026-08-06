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
    IAsyncEnumerable<IndexingProgress> IndexAsync(
        string path,
        bool recursive = true,
        CancellationToken cancellationToken = default);
}
