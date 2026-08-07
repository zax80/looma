using Microsoft.Extensions.AI;

namespace Looma.Infrastructure.Llm;

#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is marked [Experimental] in the OpenAI SDK.

/// <summary>
/// Wraps a chat client to force <c>reasoning_effort=none</c> on every
/// request — see <see cref="ModelEndpointOptions.DisableThinking"/> for why.
///
/// Ollama's native <c>think: false</c> request field does nothing on the
/// OpenAI-compatible endpoint this project talks to; the equivalent there is
/// <c>reasoning_effort</c>. <see cref="ChatOptions"/> has no built-in
/// concept of reasoning effort, so this goes through
/// <see cref="ChatOptions.RawRepresentationFactory"/> to hand the OpenAI SDK
/// a real <c>OpenAI.Chat.ChatCompletionOptions</c> with
/// <c>ReasoningEffortLevel</c> set directly — the documented mechanism for
/// exactly this "the abstraction doesn't have a typed property yet"
/// situation.
///
/// Deliberately only known inside Infrastructure.Llm: Application must stay
/// vendor-agnostic, so <c>AnswerUseCase</c> never sees
/// <c>OpenAI.Chat.ChatCompletionOptions</c> — it just calls
/// <see cref="IChatClient.GetStreamingResponseAsync"/> normally and this
/// decorator does the rest underneath.
/// </summary>
internal sealed class ReasoningEffortChatClient : DelegatingChatClient
{
    public ReasoningEffortChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, WithReasoningDisabled(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, WithReasoningDisabled(options), cancellationToken);

    /// <summary>
    /// Copies through the options this codebase actually sets today
    /// (<c>MaxOutputTokens</c>, <c>Temperature</c>) plus the
    /// reasoning-effort override. If callers start setting other
    /// <see cref="ChatOptions"/> properties in the future, add them here too
    /// — this isn't a general clone. Missed once already: <c>Temperature</c>
    /// was added to <c>AnswerUseCase</c> without updating this copy, which
    /// would have silently discarded it on every request since
    /// <c>DisableThinking</c> defaults to true.
    /// </summary>
    private static ChatOptions WithReasoningDisabled(ChatOptions? options) => new()
    {
        MaxOutputTokens = options?.MaxOutputTokens,
        Temperature = options?.Temperature,
        RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
        {
            ReasoningEffortLevel = OpenAI.Chat.ChatReasoningEffortLevel.None
        }
    };
}
