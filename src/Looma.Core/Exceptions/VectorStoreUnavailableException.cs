namespace Looma.Core.Exceptions;

/// <summary>
/// Thrown by an <see cref="Abstractions.IVectorStore"/> implementation when
/// the backing store itself can't be reached at all — connection refused,
/// DNS failure, or a timeout before any response ever came back. Distinct
/// from an implementation's own "the store responded, but with an error"
/// exception (e.g. Qdrant's own <c>QdrantRequestException</c>, which stays
/// in the Qdrant infrastructure project since it's specific to that
/// implementation's error shape) — this one is implementation-agnostic and
/// lives in Looma.Core deliberately, so it can be caught wherever it's
/// useful without needing a reference to a concrete Infrastructure.*
/// project. In particular: <c>Looma.MCP.Server</c>'s <c>Tools/</c> layer is
/// only allowed to depend on <c>Looma.Application</c>'s use-case interfaces
/// and <c>Looma.Core</c> — never a concrete Infrastructure.* namespace
/// directly (see <c>Looma.MCP.Server.csproj</c>'s own comment) — so this
/// couldn't live in the Qdrant project and still be catchable there.
///
/// A real, reproduced case this exists for: with Qdrant stopped, a chat
/// request's underlying <c>HttpRequestException</c>/<c>SocketException</c>
/// used to propagate all the way up through <c>RagRetrieval</c> and
/// <c>ChatCompletionUseCase</c> completely unhandled. That was harmless in
/// Standalone mode — <c>MainPage</c>'s generic exception handler already
/// surfaces whatever <c>Exception.Message</c> says — but in McpClient mode
/// the MCP SDK sanitizes any UNRECOGNIZED exception thrown from inside a
/// tool into a generic <c>"An error occurred invoking 'looma_chat'"</c>,
/// with zero indication Qdrant is the actual problem. Catching this
/// specific, well-known type in each MCP tool and rethrowing it as an
/// <c>McpException</c> (whose <c>Message</c> the SDK deliberately DOES
/// propagate to the caller) fixes that without weakening the SDK's
/// sensible default of not leaking arbitrary internal exception details to
/// a remote client.
/// </summary>
public sealed class VectorStoreUnavailableException : Exception
{
    public VectorStoreUnavailableException(string message) : base(message)
    {
    }

    public VectorStoreUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
