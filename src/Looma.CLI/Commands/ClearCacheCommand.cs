using Looma.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.CLI.Commands;

/// <summary>
/// Wipes the answer cache (both the exact-match file and the semantic Qdrant
/// collection). Needed because the cache's staleness check only tracks
/// re-indexing (see <see cref="IAnswerCache.ClearAsync"/>) — it has no way to
/// know a prompt, model setting, or config value changed, so a stale answer
/// cached before such a fix can otherwise keep being served indefinitely.
/// </summary>
public static class ClearCacheCommand
{
    public static async Task<int> RunAsync(IServiceProvider provider, string[] args)
    {
        var answerCache = provider.GetRequiredService<IAnswerCache>();
        await answerCache.ClearAsync();
        Console.WriteLine("Answer cache cleared.");
        return 0;
    }
}
