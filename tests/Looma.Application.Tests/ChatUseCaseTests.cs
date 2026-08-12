using System.Runtime.CompilerServices;
using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Covers <see cref="ChatUseCase.SendMessageAsync"/>'s turn-persistence
/// behavior specifically — see its class doc comment's "Balanced turns,
/// even on failure" section for the real, reproduced bug this guards
/// against (a failed turn used to leave a dangling User message with no
/// matching Assistant reply, which broke every later turn's prompt shape).
/// </summary>
public sealed class ChatUseCaseTests
{
    [Fact]
    public async Task SendMessageAsync_Success_AppendsUserThenRealAssistantMessage()
    {
        var store = new FakeChatSessionStore();
        var session = await store.CreateSessionAsync();
        var completion = new FakeChatCompletionUseCase(
            new AnswerToken { Text = "Hel", IsFinal = false },
            new AnswerToken { Text = "lo", IsFinal = false },
            new AnswerToken { Text = "", IsFinal = true, Citations = [] });
        var useCase = new ChatUseCase(completion, store);

        var tokens = new List<AnswerToken>();
        await foreach (var token in useCase.SendMessageAsync(session.Id, "Hi"))
        {
            tokens.Add(token);
        }

        Assert.Equal(3, tokens.Count);

        var stored = await store.GetSessionAsync(session.Id);
        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Messages.Count);
        Assert.Equal(ChatMessageRole.User, stored.Messages[0].Role);
        Assert.Equal("Hi", stored.Messages[0].Text);
        Assert.Equal(ChatMessageRole.Assistant, stored.Messages[1].Role);
        Assert.Equal("Hello", stored.Messages[1].Text);
    }

    [Fact]
    public async Task SendMessageAsync_GenerationFails_AppendsSyntheticAssistantMessageAndRethrows()
    {
        var store = new FakeChatSessionStore();
        var session = await store.CreateSessionAsync();
        var completion = new FakeChatCompletionUseCase(
            throwAfter: new AnswerToken { Text = "partial", IsFinal = false },
            exceptionToThrow: new InvalidOperationException("Qdrant is down"));
        var useCase = new ChatUseCase(completion, store);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in useCase.SendMessageAsync(session.Id, "Tell me about the coffee?"))
            {
            }
        });

        var stored = await store.GetSessionAsync(session.Id);
        Assert.NotNull(stored);

        // The key invariant this fix exists for: turns stay alternating.
        // A dangling User-only turn here would reproduce the original bug
        // (see ChatCompletionUseCase's "Vague follow-ups after a failed
        // prior turn" doc comment section).
        Assert.Equal(2, stored!.Messages.Count);
        Assert.Equal(ChatMessageRole.User, stored.Messages[0].Role);
        Assert.Equal(ChatMessageRole.Assistant, stored.Messages[1].Role);

        // Not a real answer — must not read as one, so a later turn's
        // grounding rule ("anything you already said") can't mistake it
        // for a stated fact.
        Assert.DoesNotContain("partial", stored.Messages[1].Text);
        Assert.Contains("failed", stored.Messages[1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendMessageAsync_Cancelled_DoesNotAppendSyntheticMessage()
    {
        var store = new FakeChatSessionStore();
        var session = await store.CreateSessionAsync();
        var completion = new FakeChatCompletionUseCase(
            throwAfter: new AnswerToken { Text = "partial", IsFinal = false },
            exceptionToThrow: new OperationCanceledException());
        var useCase = new ChatUseCase(completion, store);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in useCase.SendMessageAsync(session.Id, "Tell me about the coffee?"))
            {
            }
        });

        var stored = await store.GetSessionAsync(session.Id);
        Assert.NotNull(stored);

        // A cancelled turn isn't a "failure" this fix cares about — no
        // synthetic message should be added on top of the dangling User
        // turn; that's unrelated, pre-existing, expected behavior for an
        // abandoned turn.
        Assert.Single(stored!.Messages);
        Assert.Equal(ChatMessageRole.User, stored.Messages[0].Role);
    }

    private sealed class FakeChatCompletionUseCase : IChatCompletionUseCase
    {
        private readonly IReadOnlyList<AnswerToken> _tokens;
        private readonly AnswerToken? _throwAfter;
        private readonly Exception? _exceptionToThrow;

        public FakeChatCompletionUseCase(params AnswerToken[] tokens)
        {
            _tokens = tokens;
        }

        public FakeChatCompletionUseCase(AnswerToken throwAfter, Exception exceptionToThrow)
        {
            _tokens = [];
            _throwAfter = throwAfter;
            _exceptionToThrow = exceptionToThrow;
        }

        public async IAsyncEnumerable<AnswerToken> CompleteAsync(
            IReadOnlyList<ChatMessageEntry> history,
            string message,
            string? attachmentContext = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var token in _tokens)
            {
                await Task.Yield();
                yield return token;
            }

            if (_throwAfter is not null)
            {
                await Task.Yield();
                yield return _throwAfter;
                throw _exceptionToThrow!;
            }
        }
    }

    private sealed class FakeChatSessionStore : IChatSessionStore
    {
        private readonly Dictionary<string, ChatSession> _sessions = new();

        public Task<ChatSession> CreateSessionAsync(CancellationToken cancellationToken = default)
        {
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = "New chat",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Messages = []
            };
            _sessions[session.Id] = session;
            return Task.FromResult(session);
        }

        public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatSessionSummary>>([]);

        public Task AppendMessageAsync(string sessionId, ChatMessageEntry message, CancellationToken cancellationToken = default)
        {
            var session = _sessions[sessionId];
            _sessions[sessionId] = session with { Messages = [.. session.Messages, message], UpdatedAtUtc = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }
    }
}
