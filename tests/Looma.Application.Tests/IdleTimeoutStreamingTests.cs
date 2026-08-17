using System.Runtime.CompilerServices;
using Looma.Application.Internal;
using Microsoft.Extensions.AI;
using Xunit;

namespace Looma.Application.Tests;

public sealed class IdleTimeoutStreamingTests
{
    [Fact]
    public async Task StreamAsync_YieldsEachChunkAsItArrives()
    {
        var chatClient = new FakeStreamingChatClient(ChunksImmediately);

        var received = new List<string>();
        await foreach (var chunk in IdleTimeoutStreaming.StreamAsync(
            chatClient, [], new ChatOptions(), idleTimeoutSeconds: 5, CancellationToken.None))
        {
            received.Add(chunk);
        }

        Assert.Equal(["Hello ", "world."], received);
    }

    [Fact]
    public async Task StreamAsync_GapBetweenChunksExceedsIdleTimeout_ThrowsTimeoutExceptionNotOperationCanceled()
    {
        var chatClient = new FakeStreamingChatClient(FirstChunkThenStalls);
        var received = new List<string>();

        // See IdleTimeoutStreaming's doc comment — this must be TimeoutException,
        // specifically NOT OperationCanceledException, so ChatUseCase's
        // "cancellation isn't a real failure" handling doesn't swallow it.
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var chunk in IdleTimeoutStreaming.StreamAsync(
                chatClient, [], new ChatOptions(), idleTimeoutSeconds: 1, CancellationToken.None))
            {
                received.Add(chunk);
            }
        });

        // The chunk that arrived before the stall must still have been observed.
        Assert.Equal(["Hello "], received);
    }

    [Fact]
    public async Task StreamAsync_CallerCancels_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var chatClient = new FakeStreamingChatClient(NeverYields);

        cts.Cancel();

        // A genuine caller-side cancellation (not the internal idle timeout)
        // must still surface as a plain OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in IdleTimeoutStreaming.StreamAsync(
                chatClient, [], new ChatOptions(), idleTimeoutSeconds: 90, cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task StreamAsync_EmptyTextChunksAreSkipped()
    {
        var chatClient = new FakeStreamingChatClient(IncludesEmptyChunks);

        var received = new List<string>();
        await foreach (var chunk in IdleTimeoutStreaming.StreamAsync(
            chatClient, [], new ChatOptions(), idleTimeoutSeconds: 5, CancellationToken.None))
        {
            received.Add(chunk);
        }

        Assert.Equal(["a", "b"], received);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ChunksImmediately(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "world.");
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FirstChunkThenStalls(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello ");
        // Never completes on its own — only reacts to the token it's given,
        // exactly like a real stalled/looping generation call would.
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "unreachable");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> NeverYields(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "unreachable");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> IncludesEmptyChunks(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "a");
        yield return new ChatResponseUpdate(ChatRole.Assistant, string.Empty);
        yield return new ChatResponseUpdate(ChatRole.Assistant, (string?)null);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "b");
        await Task.CompletedTask;
    }

    private sealed class FakeStreamingChatClient(
        Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("IdleTimeoutStreaming never buffers — this fake shouldn't be called this way.");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            stream(cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
