using Looma.Application.Internal;
using Microsoft.Extensions.AI;
using Xunit;

namespace Looma.Application.Tests;

public sealed class BufferedGenerationTests
{
    [Fact]
    public async Task GenerateAsync_CompletesWithinTimeout_ReturnsAnswerText()
    {
        var chatClient = new FakeChatClient(_ => Task.FromResult(MakeResponse("hello")));

        var result = await BufferedGeneration.GenerateAsync(
            chatClient, [], new ChatOptions(), timeoutSeconds: 5, CancellationToken.None);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GenerateAsync_ExceedsTimeout_ThrowsTimeoutExceptionNotOperationCanceled()
    {
        // Never completes on its own — only reacts to the token it's given,
        // exactly like a real stalled/looping generation call would.
        var chatClient = new FakeChatClient(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return MakeResponse("unreachable");
        });

        // See BufferedGeneration's doc comment — this must be TimeoutException,
        // specifically NOT OperationCanceledException, so ChatUseCase's
        // "cancellation isn't a real failure" handling doesn't swallow it.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            BufferedGeneration.GenerateAsync(chatClient, [], new ChatOptions(), timeoutSeconds: 1, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_CallerCancels_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var chatClient = new FakeChatClient(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return MakeResponse("unreachable");
        });

        cts.Cancel();

        // A genuine caller-side cancellation (not the internal timeout)
        // must still surface as a plain OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufferedGeneration.GenerateAsync(chatClient, [], new ChatOptions(), timeoutSeconds: 90, cts.Token));
    }

    private static ChatResponse MakeResponse(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private sealed class FakeChatClient(Func<CancellationToken, Task<ChatResponse>> respond) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            respond(cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("BufferedGeneration never streams — this fake shouldn't be called this way.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
