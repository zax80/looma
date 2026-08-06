using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>Embeds a query and streams scored matches from a vector collection.</summary>
public interface ISearchUseCase
{
    IAsyncEnumerable<VectorSearchResult> SearchAsync(
        string query,
        VectorCollection collection = VectorCollection.Documents,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
