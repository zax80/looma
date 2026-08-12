using Looma.Core.Exceptions;
using ModelContextProtocol;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Rethrows a well-known Core-level failure as an <see cref="McpException"/>
/// so its message actually reaches the calling client — an untranslated
/// exception gets sanitized by the MCP SDK into a generic
/// <c>"An error occurred invoking 'x'"</c> with no actionable detail (that's
/// the SDK's correct default for exceptions it doesn't recognize — it
/// doesn't know whether a given exception's message is safe to hand to a
/// remote caller). <see cref="McpException"/> is the SDK's own designated
/// type for a message that IS meant to cross the wire.
///
/// Only <see cref="VectorStoreUnavailableException"/> is handled today —
/// see its own doc comment for the real, reproduced case that motivated
/// this (stopping Qdrant and asking a chat question). Each tool method
/// calls this from its own catch clause; anything not recognized here is
/// left to propagate and fall through to the SDK's default (safe, if
/// unhelpful) generic handling — deliberately opt-in per exception type
/// rather than a blanket catch-and-forward, so a genuinely unexpected
/// failure never accidentally leaks internal detail to a remote client.
/// </summary>
public static class ToolErrorTranslation
{
    public static McpException Translate(VectorStoreUnavailableException ex) => new(ex.Message, ex);
}
