using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>Persists saved-answer artefacts. Same durability expectation as <see cref="IChatSessionStore"/>.</summary>
public interface ISavedAnswerStore
{
    Task SaveAsync(SavedAnswer answer, CancellationToken cancellationToken = default);

    /// <summary>Newest-saved first.</summary>
    Task<IReadOnlyList<SavedAnswer>> ListAsync(CancellationToken cancellationToken = default);

    Task<SavedAnswer?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
