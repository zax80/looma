using System.Text.Json;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.Options;

namespace Looma.Infrastructure.LocalStore;

/// <summary>
/// <see cref="IChatSessionStore"/> backed by one local JSON file — a
/// dictionary of session id to <see cref="ChatSession"/>. Same
/// read-modify-write-under-a-lock shape as QdrantAnswerCache's exact-match
/// file layer, including corrupt/unreadable-file-becomes-empty fallback.
/// Not built for a large number of sessions (loads/rewrites the whole
/// file on every write) — fine for a single local user's chat history,
/// revisit if that assumption stops holding.
/// </summary>
public sealed class FileChatSessionStore : IChatSessionStore
{
    private const int TitleMaxLength = 60;

    private readonly ChatHistoryOptions _options;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public FileChatSessionStore(IOptions<ChatHistoryOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ChatSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChatSession
        {
            Id = Guid.NewGuid().ToString(),
            Title = "New chat",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Messages = []
        };

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessions = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            sessions[session.Id] = session;
            await SaveNoLockAsync(sessions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }

        return session;
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return sessions.GetValueOrDefault(sessionId);
    }

    public async Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return sessions.Values
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Select(s => new ChatSessionSummary { Id = s.Id, Title = s.Title, UpdatedAtUtc = s.UpdatedAtUtc })
            .ToList();
    }

    public async Task AppendMessageAsync(string sessionId, ChatMessageEntry message, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessions = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                throw new InvalidOperationException($"Chat session '{sessionId}' not found.");
            }

            var isFirstUserMessage = session.Messages.Count == 0 && message.Role == ChatMessageRole.User;
            var updated = session with
            {
                Title = isFirstUserMessage ? DeriveTitle(message.Text) : session.Title,
                UpdatedAtUtc = message.CreatedAtUtc,
                Messages = [.. session.Messages, message]
            };

            sessions[sessionId] = updated;
            await SaveNoLockAsync(sessions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessions = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Remove(sessionId))
            {
                await SaveNoLockAsync(sessions, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static string DeriveTitle(string firstMessageText)
    {
        var trimmed = firstMessageText.Trim();
        return trimmed.Length <= TitleMaxLength ? trimmed : trimmed[..TitleMaxLength] + "…";
    }

    private async Task<Dictionary<string, ChatSession>> LoadAsync(CancellationToken cancellationToken)
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

    private async Task<Dictionary<string, ChatSession>> LoadNoLockAsync(CancellationToken cancellationToken)
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
                .DeserializeAsync<Dictionary<string, ChatSession>>(stream, LocalStoreJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return loaded ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable file — treat as empty rather than crashing chat.
            return [];
        }
    }

    private async Task SaveNoLockAsync(Dictionary<string, ChatSession> sessions, CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, sessions, LocalStoreJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private string ResolvePath() => Path.GetFullPath(_options.SessionsFilePath);
}
