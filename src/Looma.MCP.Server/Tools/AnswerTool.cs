using System.ComponentModel;
using System.Text;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IAnswerUseCase"/>. Each generated token is forwarded as
/// an MCP progress notification as it streams from the model, so a client
/// watching progress sees the answer appear incrementally rather than only
/// once generation finishes; the final tool result carries the complete
/// answer text plus formatted citations.
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

        await foreach (var token in answerUseCase.AnswerAsync(question, cancellationToken).ConfigureAwait(false))
        {
            answerText.Append(token.Text);

            progress.Report(new ProgressNotificationValue
            {
                Progress = answerText.Length,
                Message = token.Text
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
