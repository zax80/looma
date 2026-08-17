using Looma.Application.UseCases;
using Looma.Core.Entities;
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
            if (token.IsStatus)
            {
                // A real stage transition ("Generating answer...", "No
                // local match found — searching the web...") — see
                // AnswerToken.IsStatus's doc comment. Printed on its own
                // line, not concatenated via Console.Write like content:
                // by construction (see AnswerUseCase's EnableWebSearch
                // branch), a status token never arrives after content has
                // already started printing for the SAME attempt — content
                // is either live-streamed with nothing held back, or fully
                // withheld as a refusal, never a mix — so this never has
                // to interrupt a partially-written line.
                Console.WriteLine(token.Text);
                continue;
            }

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

                    // A web result's SourceId is already the URL — no line
                    // range to append (StartLine/EndLine are null for
                    // MediaType.Web; see WebSearchFallback).
                    var location = citation.Metadata.MediaType == MediaType.Web
                        ? string.Empty
                        : $" (lines {citation.Metadata.StartLine}-{citation.Metadata.EndLine})";
                    Console.WriteLine($"  [{i + 1}] {citation.SourceId}{location}");
                }
            }
        }

        return 0;
    }
}
