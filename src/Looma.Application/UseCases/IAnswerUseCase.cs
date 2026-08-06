using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>Retrieval-augmented answer generation. Streams tokens as generated; citations arrive on the final token.</summary>
public interface IAnswerUseCase
{
    IAsyncEnumerable<AnswerToken> AnswerAsync(
        string question,
        CancellationToken cancellationToken = default);
}
