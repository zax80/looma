using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using Looma.Core.Exceptions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IAnswerUseCase"/>. Each generated <see cref="AnswerToken"/>
/// is forwarded as an MCP progress notification as it streams from the
/// model — serialized as-is (see <see cref="Wire"/>), so Looma.MCP.Client
/// deserializes straight back into the same type — so a client watching
/// progress sees the answer appear incrementally rather than only once
/// generation finishes; the final tool result carries the complete answer
/// text plus formatted citations for human/Inspector consumers.
///
/// The one field deliberately stripped before sending: each citation's
/// <see cref="DocumentChunk.Embedding"/>. It's meaningless to a remote
/// consumer and there's no reason to push a few thousand floats per
/// citation over the wire just because the record happens to carry one.
/// </summary>
[McpServerToolType]
public static class AnswerTool
{
    [McpServerTool(Name = "looma_answer", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Answers a question using retrieval-augmented generation over Looma's indexed documents, images, and audio. Returns the generated answer text followed by a citations list; refuses to answer (says so explicitly) if nothing relevant enough was indexed.")]
    public static async Task<string> Answer(
        IAnswerUseCase answerUseCase,
        IProgress<ProgressNotificationValue> progress,
        [Description("The question to answer.")] string question,
        CancellationToken cancellationToken = default)
    {
        var answerText = new StringBuilder();
        IReadOnlyList<DocumentChunk>? citations = null;

        try
        {
            await foreach (var token in answerUseCase.AnswerAsync(question, cancellationToken).ConfigureAwait(false))
            {
                answerText.Append(token.Text);

                // Strip embedding vectors before this crosses the wire — see the
                // class doc comment. `with` on a record only rewrites the
                // top-level Citations reference; token.Citations itself (and the
                // real DocumentChunk instances used elsewhere in-process) are untouched.
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
        }
        catch (VectorStoreUnavailableException ex)
        {
            throw ToolErrorTranslation.Translate(ex);
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
