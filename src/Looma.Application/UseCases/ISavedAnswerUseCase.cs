using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>Saving/listing/deleting "artefacts" — answers pinned outside the chat session they came from.</summary>
public interface ISavedAnswerUseCase
{
    /// <summary>Empty/whitespace <paramref name="title"/> falls back to the question text — see SavedAnswerUseCase.</summary>
    Task<SavedAnswer> SaveAsync(
        string title,
        string question,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        string? sourceSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>Newest-saved first.</summary>
    Task<IReadOnlyList<SavedAnswer>> ListAsync(CancellationToken cancellationToken = default);

    Task<SavedAnswer?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
