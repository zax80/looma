using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Looma.MCP.Client;

/// <summary>
/// Bridges an MCP tool call's progress notifications back into a real
/// <see cref="IAsyncEnumerable{T}"/> — the client-side half of the
/// real-streaming design documented on <c>Looma.MCP.Server.Tools.*</c>.
/// Each progress notification's JSON <c>Message</c> is deserialized
/// straight into <typeparamref name="T"/> (a <c>Looma.Core.Entities</c>
/// record — <c>IndexingProgress</c>, <c>VectorSearchResult</c>, or
/// <c>AnswerToken</c>) and pushed onto a channel that the returned
/// enumerable reads from, so items surface to the caller as they arrive
/// rather than only once the whole tool call finishes.
///
/// Real bug found and fixed here (not a text→image-search-specific issue —
/// this affected every use of this helper, just easiest to lose with a
/// single quick result): <see cref="Progress{T}"/> does NOT invoke its
/// callback synchronously. It captures the calling thread's
/// <see cref="SynchronizationContext"/> at construction and posts through
/// that; with none present (the normal case for a console app like
/// Looma.CLI), it falls back to queuing the callback on the ThreadPool —
/// meaning <c>Report()</c> returns before the handler has actually run. For
/// a tool call that finishes right after its last (or only) progress
/// notification, <c>RunToolCallAsync</c> below could reach
/// <c>channel.Writer.TryComplete()</c> BEFORE the queued handler got to
/// <c>channel.Writer.TryWrite(item)</c> — writing to an already-completed
/// channel silently fails (no exception, <c>TryWrite</c> just returns
/// <c>false</c>), so the item vanished with nothing surfaced anywhere on
/// either side. Fixed by using a plain synchronous <see cref="IProgress{T}"/>
/// implementation instead: its <c>Report()</c> runs the handler inline, on
/// whatever thread calls it — since the MCP SDK necessarily processes each
/// progress notification from the response stream before it can move on to
/// the final result, this guarantees every write lands in the channel
/// strictly before <c>TryComplete()</c> can run.
/// </summary>
public static class RemoteStreamHelper
{
    public static async IAsyncEnumerable<T> StreamAsync<T>(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false // progress callback invocations aren't guaranteed to be single-threaded
        });

        var progress = new SynchronousProgress<ProgressNotificationValue>(value =>
        {
            if (string.IsNullOrEmpty(value.Message))
            {
                return;
            }

            // A deserialization failure here would otherwise vanish
            // silently — surface it as a channel fault instead.
            try
            {
                var item = JsonSerializer.Deserialize<T>(value.Message, Wire.Options);
                if (item is not null)
                {
                    channel.Writer.TryWrite(item);
                }
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        // Fire-and-forget is deliberate: completion/failure is observed via
        // the channel (TryComplete / TryComplete(ex)), not by awaiting this
        // task directly, so RunToolCallAsync's own catch means the task it
        // returns never faults — nothing goes unobserved.
        _ = RunToolCallAsync();

        async Task RunToolCallAsync()
        {
            try
            {
                var result = await client
                    .CallToolAsync(toolName, arguments, progress, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsError == true)
                {
                    throw new McpException($"Tool '{toolName}' returned an error: {ExtractText(result) ?? "(no message)"}");
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public static string? ExtractText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>
    /// Runs <c>Report()</c> inline instead of <see cref="Progress{T}"/>'s
    /// default SynchronizationContext/ThreadPool dispatch — see this file's
    /// class doc comment for the real ordering bug that motivated this.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
