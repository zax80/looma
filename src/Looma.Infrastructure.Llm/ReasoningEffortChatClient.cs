using Looma.Core.Entities;
using Microsoft.Extensions.AI;

namespace Looma.Infrastructure.Llm;

#pragma warning disable OPENAI001 // ChatReasoningEffortLevel is marked [Experimental] in the OpenAI SDK.

/// <summary>
/// Wraps a chat client to set <c>reasoning_effort</c> on every request,
/// honoring a per-call <see cref="ThinkingMode"/> the caller stashes on
/// <see cref="ChatOptions.AdditionalProperties"/> under
/// <see cref="ThinkingModeChatOption.Key"/> — <c>Ask</c> maps to
/// <c>reasoning_effort=none</c>, <c>Think</c> to <c>reasoning_effort=high</c>.
/// Only registered at all when <see cref="ModelEndpointOptions.SupportsThinking"/>
/// is <c>true</c> (see <see cref="ServiceCollectionExtensions.AddLoomaChatClient"/>)
/// — for a model with no reasoning mode, this decorator is skipped entirely
/// rather than sending a field the model was never asked about, which is
/// also what makes a stray <c>Think</c> request against such a model a
/// silent no-op: nothing downstream of the use case even looks at the
/// property in that case.
///
/// A request with no <see cref="ThinkingModeChatOption.Key"/> set at all
/// (defensive — every current caller always sets it) defaults to
/// <c>Ask</c>/none, same "grounded RAG doesn't need reasoning by default"
/// stance the rest of this codebase already documents.
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
/// <c>OpenAI.Chat.ChatCompletionOptions</c> — it just sets the plain
/// <see cref="ThinkingMode"/> property on <see cref="ChatOptions"/> (via
/// <see cref="ChatOptions.AdditionalProperties"/>) and calls
/// <see cref="IChatClient.GetStreamingResponseAsync"/> normally; this
/// decorator does the rest underneath.
/// </summary>
internal sealed class ReasoningEffortChatClient : DelegatingChatClient
{
    public ReasoningEffortChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, WithReasoningEffort(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, WithReasoningEffort(options), cancellationToken);

    /// <summary>
    /// Copies through the options this codebase actually sets today
    /// (<c>MaxOutputTokens</c>, <c>Temperature</c>) plus the
    /// reasoning-effort override. If callers start setting other
    /// <see cref="ChatOptions"/> properties in the future, add them here too
    /// — this isn't a general clone. Missed once already: <c>Temperature</c>
    /// was added to <c>AnswerUseCase</c> without updating this copy, which
    /// would have silently discarded it on every request since this
    /// decorator is now always in the chain whenever <c>SupportsThinking</c>
    /// is true (previously: whenever <c>DisableThinking</c> defaulted true).
    /// </summary>
    private static ChatOptions WithReasoningEffort(ChatOptions? options)
    {
        var thinkingMode = options?.AdditionalProperties?.TryGetValue(ThinkingModeChatOption.Key, out var raw) == true
            && raw is ThinkingMode mode
            ? mode
            : ThinkingMode.Ask;

        var effortLevel = thinkingMode == ThinkingMode.Think
            ? OpenAI.Chat.ChatReasoningEffortLevel.High
            : OpenAI.Chat.ChatReasoningEffortLevel.None;

        return new ChatOptions
        {
            MaxOutputTokens = options?.MaxOutputTokens,
            Temperature = options?.Temperature,
            RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions
            {
                ReasoningEffortLevel = effortLevel
            }
        };
    }
}
