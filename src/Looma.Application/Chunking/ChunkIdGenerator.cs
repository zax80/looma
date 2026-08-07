using System.Security.Cryptography;
using System.Text;

namespace Looma.Application.Chunking;

/// <summary>
/// Deterministic Qdrant point ids, one per (source, chunk index) pair — see
/// <see cref="Looma.Application.UseCases.IndexingUseCase"/> for why: without
/// this, re-indexing an unchanged file assigned every chunk a fresh random
/// id and duplicated it in the vector store on every run. Same source +
/// same chunk index always produces the same id, so upsert overwrites the
/// existing point instead of adding a new one.
///
/// Qdrant point ids must be an unsigned integer or UUID string (not an
/// arbitrary string), so this hashes "sourceId#chunkIndex" into
/// UUID-shaped bytes rather than using that string directly.
///
/// Pure and static — no I/O — so it's unit-testable without a real vector store.
/// </summary>
public static class ChunkIdGenerator
{
    public static string Generate(string sourceId, int chunkIndex)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceId}#{chunkIndex}"));
        return new Guid(hash[..16]).ToString();
    }
}
