using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.CLI.Commands;

public static class CountCommand
{
    public static async Task<int> RunAsync(IServiceProvider provider, string[] args)
    {
        var collection = VectorCollection.Documents;

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--collection")
            {
                continue;
            }

            if (!Enum.TryParse(args[i + 1], ignoreCase: true, out collection))
            {
                Console.Error.WriteLine($"Unknown collection '{args[i + 1]}'. Expected 'documents' or 'images'.");
                return 1;
            }
        }

        var countUseCase = provider.GetRequiredService<ICountUseCase>();
        var count = await countUseCase.CountAsync(collection);

        // Explicitly "chunks", not "documents" — this counts vectors in the
        // collection, and one source file becomes several overlapping
        // chunks. Reporting it as a document count would misrepresent what
        // the number means (see chat history: this confused a real user).
        var noun = count == 1 ? "chunk" : "chunks";
        Console.WriteLine($"{collection}: {count} {noun}");
        return 0;
    }
}
