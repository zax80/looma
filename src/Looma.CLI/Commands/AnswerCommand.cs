using Looma.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.CLI.Commands;

public static class AnswerCommand
{
    public static async Task<int> RunAsync(IServiceProvider provider, string[] args)
    {
        var question = string.Join(' ', args);
        if (string.IsNullOrWhiteSpace(question))
        {
            Console.Error.WriteLine("""Usage: looma answer "<question>" """);
            return 1;
        }

        var answerUseCase = provider.GetRequiredService<IAnswerUseCase>();

        await foreach (var token in answerUseCase.AnswerAsync(question))
        {
            if (!token.IsFinal)
            {
                // Written as it streams in, not buffered — this is the point
                // of AnswerToken being a stream at all.
                Console.Write(token.Text);
                continue;
            }

            Console.WriteLine();

            if (token.Citations is { Count: > 0 })
            {
                Console.WriteLine();
                Console.WriteLine("Sources:");
                for (var i = 0; i < token.Citations.Count; i++)
                {
                    var citation = token.Citations[i];
                    Console.WriteLine($"  [{i + 1}] {citation.SourceId} (lines {citation.Metadata.StartLine}-{citation.Metadata.EndLine})");
                }
            }
        }

        return 0;
    }
}
