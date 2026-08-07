using System.ComponentModel;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>Wraps <see cref="ISearchUseCase"/>. Streams one progress notification per scored match as it arrives.</summary>
[McpServerToolType]
public static class SearchTool
{
    [McpServerTool(Name = "looma_search", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Searches Looma's vector store for chunks relevant to a query and returns them with relevance scores and source locations. Use collection=\"documents\" (the default) for text, image-caption/OCR, and audio-transcript search. collection=\"images\" searches raw CLIP image embeddings — there's no CLIP text encoder wired up yet, so a natural-language query against \"images\" will fail with a dimension-mismatch error rather than returning results.")]
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
                Message = line
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
