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

        var progress = new Progress<ProgressNotificationValue>(value =>
        {
            if (string.IsNullOrEmpty(value.Message))
            {
                return;
            }

            var item = JsonSerializer.Deserialize<T>(value.Message, Wire.Options);
            if (item is not null)
            {
                channel.Writer.TryWrite(item);
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
}
