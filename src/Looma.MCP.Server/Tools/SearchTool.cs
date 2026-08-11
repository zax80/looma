using System.ComponentModel;
using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="ISearchUseCase"/>. Streams one progress notification per
/// scored match as it arrives — each notification's <c>Message</c> is the
/// actual <see cref="VectorSearchResult"/> serialized as-is (see
/// <see cref="Wire"/>), so Looma.MCP.Client can deserialize straight back
/// into the same type rather than re-parsing human-readable text.
/// </summary>
[McpServerToolType]
public static class SearchTool
{
    [McpServerTool(Name = "looma_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches Looma's vector store for chunks relevant to a query and returns them with relevance scores and source locations. Use collection=\"documents\" (the default) for text, image-caption/OCR, and audio-transcript search. collection=\"images\" searches raw CLIP image embeddings using CLIP's text encoder — only available if Models.ImageEmbeddingModel.TextTower is configured; otherwise fails with a clear \"not configured\" error.")]
    public static async Task<string> Search(
        ISearchUseCase searchUseCase,
        IProgress<ProgressNotificationValue> progress,
        [Description("The search query text.")] string query,
        [Description("\"documents\" or \"images\". Defaults to \"documents\".")] string collection = "documents",
        [Description("Maximum number of results to return. Defaults to 5.")] int topK = 5,
        [Description("Minimum relevance score (0-1) a result must meet to be included. Omit to use the server's configured default threshold.")] float? minRelevanceScore = null,
        CancellationToken cancellationToken = default)
    {
        var parsedCollection = VectorCollectionParser.Parse(collection);

        var results = new List<string>();
        var count = 0;

        await foreach (var result in searchUseCase
                           .SearchAsync(query, parsedCollection, topK, minRelevanceScore, cancellationToken)
                           .ConfigureAwait(false))
        {
            count++;
            var line = FormatResult(count, result);
            results.Add(line);

            progress.Report(new ProgressNotificationValue
            {
                Progress = count,
                Total = topK,
                Message = JsonSerializer.Serialize(result, Wire.Options)
            });
        }

        return results.Count > 0
            ? string.Join('\n', results)
            : "No results at or above the relevance threshold.";
    }

    private static string FormatResult(int index, VectorSearchResult result)
    {
        var meta = result.Metadata;
        var location = meta.MediaType switch
        {
            MediaType.Audio when meta.StartTime is not null =>
                $" [{meta.StartTime:hh\\:mm\\:ss}-{meta.EndTime:hh\\:mm\\:ss}]",
            _ when meta.StartLine is not null => $" [lines {meta.StartLine}-{meta.EndLine}]",
            _ => string.Empty
        };

        return $"{index}. score={result.Score:F4} {meta.SourcePath}{location}\n   {result.Content}";
    }
}
