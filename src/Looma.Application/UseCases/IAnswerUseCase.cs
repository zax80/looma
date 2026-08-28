using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>Retrieval-augmented answer generation. Streams tokens as generated; citations arrive on the final token.</summary>
public interface IAnswerUseCase
{
    /// <param name="thinkingMode">
    /// Defaults to <see cref="ThinkingMode.Ask"/> — grounded RAG Q&amp;A
    /// doesn't need the model to reason by default. Pass
    /// <see cref="ThinkingMode.Think"/> to opt this one request into the
    /// model's reasoning mode, when the configured <c>BaseModel</c>
    /// supports it (see <see cref="IChatModelCapabilities"/>); a no-op
    /// otherwise.
    /// </param>
    IAsyncEnumerable<AnswerToken> AnswerAsync(
        string question,
        ThinkingMode thinkingMode = ThinkingMode.Ask,
        CancellationToken cancellationToken = default);
}
