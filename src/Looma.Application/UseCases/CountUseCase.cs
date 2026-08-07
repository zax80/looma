using Looma.Core.Abstractions;

namespace Looma.Application.UseCases;

public sealed class CountUseCase : ICountUseCase
{
    private readonly IVectorStore _vectorStore;

    public CountUseCase(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore;
    }

    public Task<long> CountAsync(VectorCollection collection, CancellationToken cancellationToken = default) =>
        _vectorStore.CountAsync(collection, cancellationToken);
}
