using Looma.Application.UseCases;
using Looma.Core.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.CLI.Commands;

/// <summary>Only talks to <see cref="IIndexingUseCase"/> and Core entities — no Infrastructure.* reference here.</summary>
public static class IndexCommand
{
    public static async Task<int> RunAsync(IServiceProvider provider, string[] args, IConfiguration configuration)
    {
        var recursive = !args.Contains("--no-recursive");
        var clearFirst = args.Contains("--clear");
        var path = args.FirstOrDefault(a => a is not ("--no-recursive" or "--clear"))
            ?? configuration["RAG:Sources:0:Path"];

        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Usage: looma index <path> [--no-recursive] [--clear]");
            Console.Error.WriteLine("(no path given, and RAG.Sources[0].Path isn't set in config.json either)");
            return 1;
        }

        if (clearFirst)
        {
            Console.WriteLine("Clearing the documents and images collections before indexing (--clear)...");
        }

        var indexingUseCase = provider.GetRequiredService<IIndexingUseCase>();

        var indexed = 0;
        var skipped = 0;
        var failed = 0;
        var chunksTotal = 0;

        await foreach (var progress in indexingUseCase.IndexAsync(path, recursive, clearFirst))
        {
            var prefix = progress.TotalFiles is { } total
                ? $"[{progress.FileIndex + 1}/{total}]"
                : string.Empty;

            switch (progress.Status)
            {
                case IndexingStatus.Completed:
                    indexed++;
                    chunksTotal += progress.ChunksIndexed;
                    Console.WriteLine($"{prefix} OK      {progress.FilePath} ({progress.ChunksIndexed} chunks)");
                    break;
                case IndexingStatus.Skipped:
                    skipped++;
                    Console.WriteLine($"{prefix} SKIP    {progress.FilePath} — {progress.ErrorMessage}");
                    break;
                case IndexingStatus.Failed:
                    failed++;
                    Console.Error.WriteLine($"{prefix} FAILED  {progress.FilePath} — {progress.ErrorMessage}");
                    break;
                default:
                    Console.WriteLine($"{prefix} {progress.Status}  {progress.FilePath}");
                    break;
            }
        }

        Console.WriteLine($"Done. {indexed} indexed ({chunksTotal} chunks), {skipped} skipped, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }
}
