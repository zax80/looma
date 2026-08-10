using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IChatCompletionUseCase"/> — the stateless generation
/// core of multi-turn chat. Deliberately session-less on the server: the
/// caller (Looma.MCP.Client's <c>RemoteChatUseCase</c>) owns session
/// persistence locally (the same <c>IChatSessionStore</c> Standalone mode
/// uses) and sends the relevant history explicitly on every call — see
/// <see cref="IChatCompletionUseCase"/>'s doc comment for why sessions
/// never live server-side.
///
/// History travels as a JSON string parameter (<c>historyJson</c>), not a
/// native array/object tool parameter — deserialized manually into
/// <c>List&lt;ChatMessageEntry&gt;</c> using the same <see cref="Wire"/>
/// options every other streamed payload here uses, rather than relying on
/// the SDK's schema-driven binding for a nested object array, which none
/// of the other tools have needed yet. Same progress-streaming shape as
/// <see cref="AnswerTool"/>: each <see cref="AnswerToken"/> is forwarded as
/// it's generated, with citation embeddings stripped before crossing the
/// wire.
/// </summary>
[McpServerToolType]
public static class ChatTool
{
    [McpServerTool(Name = "looma_chat", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Generates one grounded reply in a multi-turn conversation, given the prior turns and a new message. Streams the answer as progress notifications; the final result is the complete answer text plus citations. Stateless — the caller supplies the full history on every call; this tool does not persist or track chat sessions itself.")]
    public static async Task<string> Chat(
        IChatCompletionUseCase chatCompletionUseCase,
        IProgress<ProgressNotificationValue> progress,
        [Description("JSON array of prior ChatMessageEntry turns, oldest first (use \"[]\" for a new conversation).")] string historyJson,
        [Description("The new user message.")] string message,
        [Description("Optional extra context for this turn only, e.g. a caption for an image the user attached. Not embedded for retrieval.")] string? attachmentContext,
        CancellationToken cancellationToken = default)
    {
        List<ChatMessageEntry> history;
        try
        {
            history = JsonSerializer.Deserialize<List<ChatMessageEntry>>(historyJson, Wire.Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new McpException($"historyJson isn't valid JSON: {ex.Message}");
        }

        var answerText = new StringBuilder();
        IReadOnlyList<DocumentChunk>? citations = null;

        await foreach (var token in chatCompletionUseCase
            .CompleteAsync(history, message, attachmentContext, cancellationToken)
            .ConfigureAwait(false))
        {
            answerText.Append(token.Text);

            // Strip embedding vectors before this crosses the wire — same
            // reasoning as AnswerTool.
            var wireToken = token.Citations is null
                ? token
                : token with { Citations = token.Citations.Select(c => c with { Embedding = null }).ToList() };

            progress.Report(new ProgressNotificationValue
            {
                Progress = answerText.Length,
                Message = JsonSerializer.Serialize(wireToken, Wire.Options)
            });

            if (token.IsFinal)
            {
                citations = token.Citations;
            }
        }

        if (citations is null || citations.Count == 0)
        {
            return answerText.ToString();
        }

        var citationLines = citations.Select((chunk, i) =>
            $"  [{i + 1}] {chunk.Metadata.SourcePath} (chunk {chunk.Metadata.ChunkIndex})");

        return $"{answerText}\n\nCitations:\n{string.Join('\n', citationLines)}";
    }
}
