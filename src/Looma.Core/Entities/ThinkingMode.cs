namespace Looma.Core.Entities;

/// <summary>
/// Per-request choice of whether the model should reason ("think") before
/// answering, or answer directly — the user-facing "Ask" vs "Think" toggle
/// (CLI <c>--think</c>, MCP tools' <c>think</c> parameter, the MAUI Desktop
/// chat UI's switch). Only meaningful when the configured <c>BaseModel</c>
/// actually supports it — see
/// <c>Looma.Application.Configuration.ChatModelOptions.SupportsThinking</c>
/// / <c>Looma.Infrastructure.Llm.ModelEndpointOptions.SupportsThinking</c>
/// (kept in sync, same config key, two minimal layer-local copies — same
/// reasoning as <c>ChatModelOptions</c>'s own doc comment). A request for
/// <see cref="Think"/> against a model that doesn't support it is silently
/// treated as <see cref="Ask"/> — see
/// <c>Looma.Infrastructure.Llm.ReasoningEffortChatClient</c>'s doc comment.
///
/// <c>Ask</c> is the default everywhere a caller doesn't explicitly choose
/// — grounded RAG Q&amp;A doesn't need reasoning by default (see
/// <c>AnswerUseCase</c>'s system prompt), so opting INTO thinking is always
/// the deliberate, per-request choice, never the ambient one.
/// </summary>
public enum ThinkingMode
{
    Ask,
    Think
}

/// <summary>
/// The <see cref="Microsoft.Extensions.AI.ChatOptions.AdditionalProperties"/>
/// key <c>Looma.Application</c>'s use cases stash the caller's
/// <see cref="ThinkingMode"/> under, and
/// <c>Looma.Infrastructure.Llm.ReasoningEffortChatClient</c> reads back —
/// a plain string constant (not a typed API) because
/// <c>Microsoft.Extensions.AI.ChatOptions</c> has no built-in concept of
/// reasoning effort to extend; this is the documented mechanism for exactly
/// that "the abstraction doesn't have a typed property yet" situation. Both
/// sides depend on <c>Looma.Core</c> already, so the key lives here rather
/// than being duplicated as a private literal in each.
/// </summary>
public static class ThinkingModeChatOption
{
    public const string Key = "Looma.ThinkingMode";
}
