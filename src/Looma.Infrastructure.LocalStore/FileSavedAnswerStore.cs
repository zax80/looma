using System.Text.Json;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.Options;

namespace Looma.Infrastructure.LocalStore;

/// <summary><see cref="ISavedAnswerStore"/> backed by one local JSON file — same shape/caveats as <see cref="FileChatSessionStore"/>.</summary>
public sealed class FileSavedAnswerStore : ISavedAnswerStore
{
    private readonly ChatHistoryOptions _options;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public FileSavedAnswerStore(IOptions<ChatHistoryOptions> options)
    {
        _options = options.Value;
    }

    public async Task SaveAsync(SavedAnswer answer, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var answers = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            answers[answer.Id] = answer;
            await SaveNoLockAsync(answers, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<SavedAnswer>> ListAsync(CancellationToken cancellationToken = default)
    {
        var answers = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return answers.Values.OrderByDescending(a => a.SavedAtUtc).ToList();
    }

    public async Task<SavedAnswer?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var answers = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return answers.GetValueOrDefault(id);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var answers = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            if (answers.Remove(id))
            {
                await SaveNoLockAsync(answers, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, SavedAnswer>> LoadAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, SavedAnswer>> LoadNoLockAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer
                .DeserializeAsync<Dictionary<string, SavedAnswer>>(stream, LocalStoreJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return loaded ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private async Task SaveNoLockAsync(Dictionary<string, SavedAnswer> answers, CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, answers, LocalStoreJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private string ResolvePath() => Path.GetFullPath(_options.SavedAnswersFilePath);
}
