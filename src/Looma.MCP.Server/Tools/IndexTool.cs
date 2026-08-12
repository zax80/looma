using System.ComponentModel;
using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using Looma.Core.Exceptions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IIndexingUseCase"/>. Each streamed
/// <see cref="IndexingProgress"/> event is forwarded immediately — serialized
/// as-is (see <see cref="Wire"/>) into the <c>Message</c> field — as an MCP
/// <c>notifications/progress</c> message via the injected
/// <see cref="IProgress{T}"/> — the SDK correlates it to the calling
/// client's own progress token automatically (a no-op if the client didn't
/// ask for progress tracking). This is the real-streaming mechanism MCP
/// tool calls actually support: a single call still returns one final
/// result, but per-file updates arrive live rather than only at the end,
/// matching <see cref="IIndexingUseCase"/>'s own "never buffer the whole
/// run" contract — Looma.MCP.Client deserializes each notification straight
/// back into an <see cref="IndexingProgress"/>, so a remote consumer gets the
/// exact same event stream a local (CLI) consumer would.
/// </summary>
[McpServerToolType]
public static class IndexTool
{
    [McpServerTool(Name = "looma_index", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Indexes a folder of documents (PDF/DOCX/XLSX/TXT/MD/CSV), images (PNG/JPG), and audio (WAV/MP3) into Looma's vector store. Streams a progress notification per file; returns a final summary once the whole folder has been processed. Idempotent: chunk ids are deterministic, so re-indexing the same folder converges on the same stored chunks rather than duplicating them.")]
    public static async Task<string> Index(
        IIndexingUseCase indexingUseCase,
        IProgress<ProgressNotificationValue> progress,
        [Description("Path to the folder to index (absolute, or relative to the server's working directory).")] string path,
        [Description("Recurse into subdirectories. Defaults to true.")] bool recursive = true,
        [Description("Wipe the documents and images collections before indexing. Destructive — off by default.")] bool clearFirst = false,
        CancellationToken cancellationToken = default)
    {
        var completed = 0;
        var skipped = 0;
        var failed = 0;
        var totalChunks = 0;
        var lines = new List<string>();

        try
        {
            await foreach (var evt in indexingUseCase.IndexAsync(path, recursive, clearFirst, cancellationToken)
                               .ConfigureAwait(false))
            {
                var line = evt.Status switch
                {
                    IndexingStatus.Completed => $"[{evt.FileIndex}/{evt.TotalFiles}] Indexed {evt.FilePath} ({evt.ChunksIndexed} chunks)",
                    IndexingStatus.Skipped => $"[{evt.FileIndex}/{evt.TotalFiles}] Skipped {evt.FilePath}" +
                                               (evt.ErrorMessage is null ? string.Empty : $" — {evt.ErrorMessage}"),
                    IndexingStatus.Failed => $"[{evt.FileIndex}/{evt.TotalFiles}] Failed {evt.FilePath} — {evt.ErrorMessage}",
                    _ => $"[{evt.FileIndex}/{evt.TotalFiles}] {evt.Status} {evt.FilePath}"
                };
                lines.Add(line);

                switch (evt.Status)
                {
                    case IndexingStatus.Completed:
                        completed++;
                        totalChunks += evt.ChunksIndexed;
                        break;
                    case IndexingStatus.Skipped:
                        skipped++;
                        break;
                    case IndexingStatus.Failed:
                        failed++;
                        break;
                }

                progress.Report(new ProgressNotificationValue
                {
                    Progress = evt.FileIndex ?? completed + skipped + failed,
                    Total = evt.TotalFiles ?? 0,
                    Message = JsonSerializer.Serialize(evt, Wire.Options)
                });
            }
        }
        catch (VectorStoreUnavailableException ex)
        {
            throw ToolErrorTranslation.Translate(ex);
        }

        lines.Add($"Done: {completed} indexed ({totalChunks} chunks), {skipped} skipped, {failed} failed.");
        return string.Join('\n', lines);
    }
}
