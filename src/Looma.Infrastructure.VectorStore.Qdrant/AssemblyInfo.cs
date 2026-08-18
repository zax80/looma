using System.Runtime.CompilerServices;

// Lets Looma.Infrastructure.VectorStore.Qdrant.Tests directly unit-test
// small internal, I/O-free logic pieces (e.g. AnswerCacheEviction,
// AnswerCacheStaleness) without making them part of this assembly's public
// surface — same pattern already used for Looma.Application ->
// Looma.Application.Tests.
[assembly: InternalsVisibleTo("Looma.Infrastructure.VectorStore.Qdrant.Tests")]
