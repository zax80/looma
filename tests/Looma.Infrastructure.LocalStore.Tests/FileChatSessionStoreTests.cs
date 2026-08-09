using Looma.Core.Entities;
using Looma.Infrastructure.LocalStore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Looma.Infrastructure.LocalStore.Tests;

public sealed class FileChatSessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileChatSessionStore _store;

    public FileChatSessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "looma-chat-tests-" + Guid.NewGuid());
        var options = Options.Create(new ChatHistoryOptions
        {
            SessionsFilePath = Path.Combine(_tempDir, "chat-sessions.json")
        });
        _store = new FileChatSessionStore(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsSessionWithNoMessages()
    {
        var session = await _store.CreateSessionAsync();

        Assert.NotEmpty(session.Id);
        Assert.Empty(session.Messages);
        Assert.Equal("New chat", session.Title);
    }

    [Fact]
    public async Task GetSessionAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _store.GetSessionAsync("does-not-exist"));
    }

    [Fact]
    public async Task AppendMessageAsync_FirstUserMessage_DerivesTitleFromIt()
    {
        var session = await _store.CreateSessionAsync();

        await _store.AppendMessageAsync(session.Id, new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.User,
            Text = "What's in the quarterly report?",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var updated = await _store.GetSessionAsync(session.Id);
        Assert.Equal("What's in the quarterly report?", updated!.Title);
        Assert.Single(updated.Messages);
    }

    [Fact]
    public async Task AppendMessageAsync_LongFirstMessage_TruncatesTitle()
    {
        var session = await _store.CreateSessionAsync();
        var longText = new string('x', 200);

        await _store.AppendMessageAsync(session.Id, new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.User,
            Text = longText,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var updated = await _store.GetSessionAsync(session.Id);
        Assert.True(updated!.Title.Length <= 61); // 60 chars + ellipsis
        Assert.EndsWith("…", updated.Title);
    }

    [Fact]
    public async Task AppendMessageAsync_SecondMessage_DoesNotOverwriteTitle()
    {
        var session = await _store.CreateSessionAsync();
        await _store.AppendMessageAsync(session.Id, new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.User,
            Text = "first question",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await _store.AppendMessageAsync(session.Id, new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.Assistant,
            Text = "the answer",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var updated = await _store.GetSessionAsync(session.Id);
        Assert.Equal("first question", updated!.Title);
        Assert.Equal(2, updated.Messages.Count);
    }

    [Fact]
    public async Task AppendMessageAsync_UnknownSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.AppendMessageAsync("does-not-exist", new ChatMessageEntry
            {
                Id = Guid.NewGuid().ToString(),
                Role = ChatMessageRole.User,
                Text = "hello",
                CreatedAtUtc = DateTimeOffset.UtcNow
            }));
    }

    [Fact]
    public async Task ListSessionsAsync_OrdersByUpdatedAtDescending()
    {
        var first = await _store.CreateSessionAsync();
        await Task.Delay(10);
        var second = await _store.CreateSessionAsync();

        var summaries = await _store.ListSessionsAsync();

        Assert.Equal(second.Id, summaries[0].Id);
        Assert.Equal(first.Id, summaries[1].Id);
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesIt()
    {
        var session = await _store.CreateSessionAsync();

        await _store.DeleteSessionAsync(session.Id);

        Assert.Null(await _store.GetSessionAsync(session.Id));
    }

    [Fact]
    public async Task DeleteSessionAsync_UnknownId_DoesNotThrow()
    {
        await _store.DeleteSessionAsync("does-not-exist");
    }

    [Fact]
    public async Task Persistence_SurvivesANewStoreInstance()
    {
        var session = await _store.CreateSessionAsync();
        await _store.AppendMessageAsync(session.Id, new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.User,
            Text = "does this survive?",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var reopened = new FileChatSessionStore(Options.Create(new ChatHistoryOptions
        {
            SessionsFilePath = Path.Combine(_tempDir, "chat-sessions.json")
        }));

        var reloaded = await reopened.GetSessionAsync(session.Id);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Messages);
    }
}
