using System.Diagnostics;
using Looma.Application;
using Looma.Application.Configuration;
using Looma.CLI.Commands;
using Looma.Core.Exceptions;
using Looma.Infrastructure.Llm;
using Looma.Infrastructure.LocalStore;
using Looma.Infrastructure.VectorStore.Qdrant;
using Looma.Infrastructure.WebSearch.SearXng;
using Looma.MCP.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using McpClientType = ModelContextProtocol.Client.McpClient;

namespace Looma.CLI;

/// <summary>
/// Composition root for both deployment shapes <c>Deployment:Mode</c>
/// supports: <c>"Standalone"</c> (local DI, talks to Qdrant/Ollama
/// directly — the only mode this project had until Looma.MCP.Client
/// existed) and <c>"McpClient"</c> (talks to a remote Looma.MCP.Server
/// instead; see <see cref="LoomaMcpConnection"/>). Per CLAUDE.md rule 1,
/// this file is the one place in Looma.CLI allowed to reference
/// Infrastructure.* or Looma.MCP.Client directly — no command handler
/// under Commands/ references either; they only see
/// <c>Looma.Application</c>'s use-case interfaces, so the same command code
/// runs unmodified regardless of which mode is active.
///
/// Config discovery is deliberately simple and explicit per CLAUDE.md —
/// "config.json" relative to the current working directory, or an explicit
/// path via <c>--config &lt;path&gt;</c>. No walking up parent directories
/// looking for it.
/// </summary>
public static class Program
{
    /// <summary>Set LOOMA_DEBUG_TIMING=1 to print per-phase startup timing to stderr (see AnswerUseCase for the same flag inside the answer path).</summary>
    private static readonly bool DebugTimingEnabled = Environment.GetEnvironmentVariable("LOOMA_DEBUG_TIMING") == "1";

    public static async Task<int> Main(string[] args)
    {
        var startupTimer = Stopwatch.StartNew();
        long lastMs = 0;
        void MarkTiming(string phase)
        {
            if (!DebugTimingEnabled)
            {
                return;
            }

            var elapsed = startupTimer.ElapsedMilliseconds;
            Console.Error.WriteLine($"[timing] {phase}: {elapsed - lastMs}ms (total {elapsed}ms)");
            lastMs = elapsed;
        }

        var (configPath, remainingArgs) = ExtractConfigOption(args);

        if (remainingArgs.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine(
                $"Config file not found: '{configPath}'. Run from a directory containing config.json, " +
                "or pass --config <path>.");
            return 1;
        }

        IConfiguration configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: false, reloadOnChange: false)
                .Build();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config from '{configPath}': {ex.Message}");
            return 1;
        }
        MarkTiming("load config");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var services = new ServiceCollection();
        services.AddSingleton(configuration);

        var deploymentMode = configuration["Deployment:Mode"];
        if (string.IsNullOrWhiteSpace(deploymentMode))
        {
            deploymentMode = "Standalone";
        }

        McpClientType? mcpClient = null;

        try
        {
            switch (deploymentMode)
            {
                case "Standalone":
                    await ConfigureStandaloneAsync(configuration, services, cts.Token, MarkTiming);
                    break;

                case "McpClient":
                    mcpClient = await ConfigureMcpClientAsync(configuration, services, cts.Token, MarkTiming);
                    break;

                default:
                    Console.Error.WriteLine(
                        $"Unknown Deployment:Mode '{deploymentMode}'. Expected \"Standalone\" or \"McpClient\".");
                    return 1;
            }
        }
        catch (OllamaLifecycleException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InferenceEndpointNotAllowedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex) when (deploymentMode == "McpClient")
        {
            // LoomaMcpConnection's own config/auth validation errors — same
            // "fail loudly at startup" spirit as the exceptions above.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (McpException ex)
        {
            Console.Error.WriteLine($"Couldn't connect to the remote MCP server: {ex.Message}");
            return 1;
        }
        catch (HttpRequestException ex) when (deploymentMode == "McpClient")
        {
            // The common real failure: nothing is listening at
            // Deployment:McpServerEndpoint yet (server not started, wrong
            // port, wrong host). McpClient.CreateAsync surfaces this as a
            // raw HttpRequestException, not an McpException — worth its own
            // clear message rather than a stack trace.
            var endpoint = configuration["Deployment:McpServerEndpoint"];
            Console.Error.WriteLine(
                $"Couldn't reach the MCP server at '{endpoint}' ({ex.Message}). " +
                "Is Looma.MCP.Server running there? See docs/mcp-server.md.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }

        // McpClient is only non-null in "McpClient" mode; AddSingleton(instance)
        // registrations are never disposed by the container itself, so this is
        // the one place responsible for shutting the connection down cleanly.
        await using var mcpClientLifetime = mcpClient;

        await using var provider = services.BuildServiceProvider();
        MarkTiming("build DI container");

        var command = remainingArgs[0];
        var commandArgs = remainingArgs[1..];

        try
        {
            var result = command switch
            {
                "index" => await IndexCommand.RunAsync(provider, commandArgs, configuration),
                "answer" => await AnswerCommand.RunAsync(provider, commandArgs),
                "count" => await CountCommand.RunAsync(provider, commandArgs),
                "search" => await SearchCommand.RunAsync(provider, commandArgs),
                "clear-cache" => await ClearCacheCommand.RunAsync(provider, commandArgs),
                "-h" or "--help" or "help" => PrintUsage(),
                _ => PrintUnknownCommand(command)
            };
            MarkTiming("command execution");
            return result;
        }
        catch (QdrantRequestException ex)
        {
            Console.Error.WriteLine($"Qdrant error: {ex.Message}");
            return 1;
        }
        catch (VectorStoreUnavailableException ex)
        {
            // Same "clean one-line error, not a raw stack-trace crash"
            // discipline as the QdrantRequestException case above — see
            // the exception's own doc comment for the real case this
            // fixes (Qdrant stopped mid-session).
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (McpException ex)
        {
            Console.Error.WriteLine($"MCP error: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }

    /// <summary>
    /// Everything standalone mode needs: local Ollama lifecycle (fatal for
    /// Base/Embedding, best-effort for Vision — see
    /// <see cref="OllamaStartup"/>), best-effort direct-download
    /// provisioning for CLIP/Whisper, then the real Infrastructure.* DI
    /// wiring. Unchanged from before Looma.MCP.Client existed — this is
    /// exactly what Program.cs always did when there was only one mode.
    /// </summary>
    private static async Task ConfigureStandaloneAsync(
        IConfiguration configuration,
        IServiceCollection services,
        CancellationToken cancellationToken,
        Action<string> markTiming)
    {
        // Standalone mode shouldn't require the person to have separately
        // started Ollama — detect/launch/pull-models here, once, before
        // anything downstream tries to talk to it.
        await OllamaStartup.EnsureOllamaReadyAsync(
            configuration,
            onStatus: message => Console.WriteLine($"[ollama] {message}"),
            confirmInstall: ConfirmInstallAsync,
            cancellationToken);
        markTiming("Ollama readiness check");

        try
        {
            // The direct-fetch half of first-run provisioning for models
            // Ollama doesn't serve (CLIP, Whisper). Deliberately best-effort,
            // not fatal — see LocalModelFileProvisioner's doc comment for why
            // this is treated differently from the Ollama readiness check above.
            await LocalModelFileProvisioner.EnsureImageEmbeddingModelReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[clip] {message}"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[clip] Warning: couldn't auto-provision the CLIP model ({ex.Message}). " +
                "Image indexing will fail until this is resolved; everything else is unaffected. " +
                "See docs/model-setup.md.");
        }
        markTiming("CLIP model provisioning check");

        try
        {
            await LocalModelFileProvisioner.EnsureSpeechToTextModelReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[whisper] {message}"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[whisper] Warning: couldn't auto-provision the Whisper model ({ex.Message}). " +
                "Audio indexing will fail until this is resolved; everything else is unaffected. " +
                "See docs/model-setup.md.");
        }
        markTiming("Whisper model provisioning check");

        try
        {
            // Genuinely optional (not just best-effort like CLIP/Whisper
            // above) — a no-op unless Models.ImageEmbeddingModel.TextTower
            // is actually configured. See its own doc comment.
            await LocalModelFileProvisioner.EnsureTextToImageSearchModelReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[clip-text] {message}"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[clip-text] Warning: couldn't auto-provision the CLIP text-tower files ({ex.Message}). " +
                "'search --collection images' with a text query will fail until this is resolved; " +
                "everything else is unaffected. See docs/model-setup.md.");
        }
        markTiming("CLIP text-tower provisioning check");

        services.AddQdrantVectorStore(configuration);
        services.AddQdrantAnswerCache(configuration);
        services.AddSearXngWebSearch(configuration);
        services.AddLoomaLocalChatStore(configuration);
        services.AddLoomaChatClient(configuration);
        services.AddLoomaEmbeddingGenerator(configuration);
        services.AddLoomaImageCaptioner(configuration);
        services.AddLoomaImageEmbeddingGenerator(configuration);
        services.AddLoomaTextToImageEmbeddingGenerator(configuration);
        services.AddLoomaAudioTranscriber(configuration);
        services.AddLoomaApplicationUseCases(configuration);
        services.AddLoomaLocalChatOrchestration();
    }

    /// <summary>
    /// McpClient mode: no local Ollama/model provisioning at all — the
    /// remote Looma.MCP.Server owns that entirely. Connects, then registers
    /// the remote use-case implementations against the exact same
    /// interfaces standalone mode registers local ones against, plus
    /// <see cref="RagOptions"/> alone (not the rest of
    /// <c>AddLoomaApplicationUseCases</c>) so <c>SearchCommand</c>'s
    /// diagnostic threshold display still works without pulling in any
    /// Infrastructure.* dependency.
    /// </summary>
    private static async Task<McpClientType> ConfigureMcpClientAsync(
        IConfiguration configuration,
        IServiceCollection services,
        CancellationToken cancellationToken,
        Action<string> markTiming)
    {
        Console.WriteLine("[mcp-client] Connecting to remote Looma.MCP.Server...");
        var client = await LoomaMcpConnection.ConnectAsync(configuration, cancellationToken);
        Console.WriteLine("[mcp-client] Connected.");
        markTiming("MCP client connection");

        services.AddOptions<RagOptions>().Bind(configuration.GetSection(RagOptions.SectionName));
        services.AddLoomaLocalChatStore(configuration);
        services.AddLoomaMcpClientUseCases(client);

        return client;
    }

    /// <summary>
    /// Never prompts in a non-interactive context (CI, piped input, a
    /// redirected console) — there'd be nothing to read, and a hang there is
    /// far worse than just failing with a clear message.
    /// </summary>
    private static Task<bool> ConfirmInstallAsync(string commandDescription)
    {
        if (Console.IsInputRedirected)
        {
            return Task.FromResult(false);
        }

        Console.WriteLine();
        Console.WriteLine("Ollama isn't installed.");
        Console.Write($"Install it now by running '{commandDescription}'? [y/N] ");
        var response = Console.ReadLine();

        return Task.FromResult(string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase));
    }

    private static (string ConfigPath, string[] RemainingArgs) ExtractConfigOption(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--config")
            {
                var configPath = args[i + 1];
                var remaining = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
                return (configPath, remaining);
            }
        }

        return (Path.Combine(Directory.GetCurrentDirectory(), "config.json"), args);
    }

    private static int PrintUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: '{command}'");
        PrintUsage();
        return 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            looma — local-first document intelligence

            Usage:
              looma [--config <path>] index <path> [--no-recursive] [--clear]
              looma [--config <path>] answer "<question>"
              looma [--config <path>] count [--collection documents|images]
              looma [--config <path>] search "<query>" [--top-k N] [--min-score X] [--collection documents|images]
              looma [--config <path>] clear-cache

            --config defaults to config.json in the current directory.
            Deployment:Mode in config.json selects "Standalone" (default —
            talks to Qdrant/Ollama directly) or "McpClient" (talks to a
            remote Looma.MCP.Server; also needs Deployment:McpServerEndpoint
            and the Mcp:Auth:ApiKeyEnvVar environment variable set).
            """);
        return 0;
    }
}
