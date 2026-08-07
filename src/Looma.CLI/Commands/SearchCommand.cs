using Looma.Application.Configuration;
using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Looma.CLI.Commands;

/// <summary>
/// Diagnostic command: shows raw retrieval results and their similarity
/// scores directly. Defaults to bypassing <c>RAG.MinRelevanceScore</c>
/// entirely (score 0) rather than silently filtering the way <c>answer</c>
/// does, so "why didn't answer find X" can be answered by looking at real
/// numbers — is the right chunk not being retrieved at all, or is it being
/// retrieved just below the configured threshold — instead of guessing.
/// </summary>
public static class SearchCommand
{
    public static async Task<int> RunAsync(IServiceProvider provider, string[] args)
    {
        var collection = VectorCollection.Documents;
        float minScore = 0f;
        var topK = 5;
        var queryParts = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--collection" when i + 1 < args.Length:
                    if (!Enum.TryParse(args[i + 1], ignoreCase: true, out collection))
                    {
                        Console.Error.WriteLine($"Unknown collection '{args[i + 1]}'. Expected 'documents' or 'images'.");
                        return 1;
                    }
                    i++;
                    break;

                case "--top-k" when i + 1 < args.Length:
                    if (!int.TryParse(args[i + 1], out topK) || topK <= 0)
                    {
                        Console.Error.WriteLine($"Invalid --top-k '{args[i + 1]}'. Expected a positive integer.");
                        return 1;
                    }
                    i++;
                    break;

                case "--min-score" when i + 1 < args.Length:
                    if (!float.TryParse(args[i + 1], out minScore))
                    {
                        Console.Error.WriteLine($"Invalid --min-score '{args[i + 1]}'. Expected a number.");
                        return 1;
                    }
                    i++;
                    break;

                default:
                    queryParts.Add(args[i]);
                    break;
            }
        }

        var query = string.Join(' ', queryParts);
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("""Usage: looma search "<query>" [--top-k N] [--min-score X] [--collection documents|images]""");
            return 1;
        }

        var searchUseCase = provider.GetRequiredService<ISearchUseCase>();
        var configuredThreshold = provider.GetRequiredService<IOptions<RagOptions>>().Value.MinRelevanceScore;

        Console.WriteLine($"RAG.MinRelevanceScore is currently {configuredThreshold:F2} — 'answer' only uses results at or above that score.");
        Console.WriteLine();

        var rank = 0;
        await foreach (var result in searchUseCase.SearchAsync(query, collection, topK, minScore))
        {
            rank++;
            var passesThreshold = result.Score >= configuredThreshold;
            var status = passesThreshold ? "used by answer" : "BELOW THRESHOLD, not used by answer";

            Console.WriteLine($"[{rank}] score={result.Score:F4} ({status})");
            Console.WriteLine($"    {result.Metadata.SourcePath} (lines {result.Metadata.StartLine}-{result.Metadata.EndLine})");
            Console.WriteLine($"    {Snippet(result.Content)}");
            Console.WriteLine();
        }

        if (rank == 0)
        {
            Console.WriteLine("(no results — the collection may be empty, or nothing scored above --min-score)");
        }

        return 0;
    }

    private static string Snippet(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "(no content)";
        }

        const int maxLength = 160;
        var singleLine = content.ReplaceLineEndings(" ");
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
