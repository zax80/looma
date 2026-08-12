using System.Runtime.CompilerServices;

// WebSearchFallback (Internal/WebSearchFallback.cs) has small, genuinely
// unit-testable branching logic (deterministic trigger, field mapping to
// DocumentChunk) with trivial dependencies (a List, RagOptions, and the
// easily-faked IWebSearchProvider) — unlike its "internal helper" siblings
// (RagRetrieval, QdrantConnectivity), which need a real IVectorStore/HTTP
// call to test meaningfully and are covered indirectly through their
// public callers instead. Testing it directly here, rather than only
// indirectly through AnswerUseCase/ChatCompletionUseCase (which would need
// full IChatClient/IEmbeddingGenerator fakes for comparatively little
// extra signal), is the better trade for this specific class.
[assembly: InternalsVisibleTo("Looma.Application.Tests")]
