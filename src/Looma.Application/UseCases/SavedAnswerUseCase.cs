using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

public sealed class SavedAnswerUseCase : ISavedAnswerUseCase
{
    private readonly ISavedAnswerStore _store;

    public SavedAnswerUseCase(ISavedAnswerStore store)
    {
        _store = store;
    }

    public async Task<SavedAnswer> SaveAsync(
        string title,
        string question,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        string? sourceSessionId,
        CancellationToken cancellationToken = default)
    {
        var answer = new SavedAnswer
        {
            Id = Guid.NewGuid().ToString(),
            Title = string.IsNullOrWhiteSpace(title) ? question : title,
            Question = question,
            AnswerText = answerText,
            Citations = citations,
            SavedAtUtc = DateTimeOffset.UtcNow,
            SourceSessionId = sourceSessionId
        };

        await _store.SaveAsync(answer, cancellationToken).ConfigureAwait(false);
        return answer;
    }

    public Task<IReadOnlyList<SavedAnswer>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    public Task<SavedAnswer?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        _store.GetAsync(id, cancellationToken);

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(id, cancellationToken);
}
