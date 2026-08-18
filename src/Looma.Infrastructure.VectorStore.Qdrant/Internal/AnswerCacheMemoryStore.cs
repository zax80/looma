namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

/// <summary>
/// Shared, in-memory cache of the exact-match layer's parsed file contents
/// — registered as a DI singleton specifically so it persists across
/// <see cref="QdrantAnswerCache"/>'s own per-call instances.
/// <c>ServiceCollectionExtensions.AddQdrantAnswerCache</c>'s
/// <c>AddHttpClient&lt;TClient,TImplementation&gt;</c> registration is
/// always Transient by design (so <c>IHttpClientFactory</c> can rotate
/// underlying handlers) — a fresh <see cref="QdrantAnswerCache"/> instance
/// exists per resolution, so any state kept ON that instance would be
/// reloaded from scratch on every single call. This object is what
/// actually persists across those instances.
///
/// A real, avoidable cost this closes: without it, every single
/// <c>TryGetExactAsync</c> call re-read AND re-parsed the entire JSON file
/// from disk, even though this process is the file's sole writer (see
/// <see cref="QdrantAnswerCache"/>'s class doc comment) and its content
/// only ever changes via that same class's own store/clear calls — a cost
/// that grows with how many questions have ever been cached, paid on every
/// single lookup, hit or miss.
///
/// The class itself is <c>public</c> only because it must be an accessible
/// constructor parameter type on the public <see cref="QdrantAnswerCache"/>
/// — its members stay <c>internal</c> (same as <see cref="ExactCacheEntry"/>,
/// the type they traffic in), so nothing outside this assembly can actually
/// read or mutate the cached entries directly.
/// </summary>
public sealed class AnswerCacheMemoryStore
{
    /// <summary>Null until the first load — <see cref="QdrantAnswerCache"/> loads from disk exactly once per process lifetime, then reads/writes this instead.</summary>
    internal Dictionary<string, ExactCacheEntry>? Entries { get; private set; }

    internal void Set(Dictionary<string, ExactCacheEntry> entries) => Entries = entries;

    internal void Clear() => Entries = null;
}
