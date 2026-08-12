using Looma.Core.Exceptions;

namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

/// <summary>
/// Shared connectivity-failure translation for every raw HTTP call this
/// project makes to Qdrant — <see cref="QdrantVectorStore"/> AND
/// <see cref="QdrantAnswerCache"/> (the semantic-cache collection lives in
/// the same Qdrant instance). Extracted here after a real, reproduced gap:
/// <see cref="QdrantAnswerCache.ClearAsync"/> originally hand-rolled its own
/// try/catch instead of using this, and only caught
/// <see cref="HttpRequestException"/> — missing the
/// <see cref="TaskCanceledException"/> case below, which is exactly what a
/// real Windows machine's network stack produced for an unreachable
/// loopback port during testing (a timeout, not an immediate connection
/// refusal, unlike what was seen on the original Linux dev sandbox).
/// Duplicating this logic once already caused it to drift out of sync;
/// one shared implementation is what actually prevents that recurring.
/// </summary>
internal static class QdrantConnectivity
{
    /// <summary>
    /// Runs an HTTP call and translates a connectivity-level failure — Qdrant
    /// simply isn't there to respond at all — into
    /// <see cref="VectorStoreUnavailableException"/>, a clear, actionable
    /// message instead of a raw <see cref="HttpRequestException"/>/
    /// <see cref="System.Net.Sockets.SocketException"/>/
    /// <see cref="TaskCanceledException"/>. See the class doc comment for
    /// why both exception types matter, and
    /// <see cref="VectorStoreUnavailableException"/>'s own doc comment for
    /// the original real-world case this whole mechanism exists for
    /// (stopping Qdrant mid-session, asking a chat question).
    ///
    /// Deliberately narrower than "any exception" — a genuine
    /// caller-requested cancellation (<paramref name="cancellationToken"/>
    /// itself signaled) is NOT translated; only a <see cref="TaskCanceledException"/>
    /// <see cref="HttpClient"/> raised on its OWN internal timeout, which
    /// the caller never asked for, counts as "unavailable" here. Qdrant
    /// responding at all — even with an error status — is a different,
    /// separate failure mode each caller handles itself (via
    /// <c>QdrantRequestException</c>), not this method.
    /// </summary>
    public static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new VectorStoreUnavailableException(BuildUnavailableMessage(action), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VectorStoreUnavailableException($"{BuildUnavailableMessage(action)} (request timed out)", ex);
        }
    }

    private static string BuildUnavailableMessage(string action) =>
        $"Can't reach Qdrant to {action} — make sure Qdrant is running and VectorStore.Endpoint in " +
        "config.json is correct. See docs/config-reference.md's VectorStore section.";
}
