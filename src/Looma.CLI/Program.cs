using System.Diagnostics;
using Looma.Application;
using Looma.CLI.Commands;
using Looma.Infrastructure.Llm;
using Looma.Infrastructure.VectorStore.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.CLI;

/// <summary>
/// Standalone-mode entry point: local DI, no MCP client. Config discovery is
/// deliberately simple and explicit per CLAUDE.md — "config.json" relative
/// to the current working directory, or an explicit path via
/// <c>--config &lt;path&gt;</c>. No walking up parent directories looking for it.
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

        try
        {
            // Standalone mode shouldn't require the person to have separately
            // started Ollama — detect/launch/pull-models here, once, before
            // anything downstream tries to talk to it.
            await OllamaStartup.EnsureOllamaReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[ollama] {message}"),
                confirmInstall: ConfirmInstallAsync,
                cts.Token);
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
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        MarkTiming("Ollama readiness check");

        try
        {
            // The direct-fetch half of first-run provisioning for models
            // Ollama doesn't serve (CLIP, Whisper). Deliberately best-effort,
            // not fatal — see LocalModelFileProvisioner's doc comment for why
            // this is treated differently from the Ollama readiness check above.
            await LocalModelFileProvisioner.EnsureImageEmbeddingModelReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[clip] {message}"),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[clip] Warning: couldn't auto-provision the CLIP model ({ex.Message}). " +
                "Image indexing will fail until this is resolved; everything else is unaffected. " +
                "See docs/model-setup.md.");
        }
        MarkTiming("CLIP model provisioning check");

        try
        {
            await LocalModelFileProvisioner.EnsureSpeechToTextModelReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[whisper] {message}"),
                cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[whisper] Warning: couldn't auto-provision the Whisper model ({ex.Message}). " +
                "Audio indexing will fail until this is resolved; everything else is unaffected. " +
                "See docs/model-setup.md.");
        }
        MarkTiming("Whisper model provisioning check");

        var services = new ServiceCollection();
        services.AddSingleton(configuration);

        try
        {
            services.AddQdrantVectorStore(configuration);
            services.AddQdrantAnswerCache(configuration);
            services.AddLoomaChatClient(configuration);
            services.AddLoomaEmbeddingGenerator(configuration);
            services.AddLoomaImageCaptioner(configuration);
            services.AddLoomaImageEmbeddingGenerator(configuration);
            services.AddLoomaAudioTranscriber(configuration);
            services.AddLoomaApplicationUseCases(configuration);
        }
        catch (InferenceEndpointNotAllowedException ex)
        {
            // Fail loudly at startup, per CLAUDE.md — this is exactly that
            // failure surfacing at the CLI boundary instead of as a stack trace.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

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
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
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
            looma — local-first document intelligence (standalone mode)

            Usage:
              looma [--config <path>] index <path> [--no-recursive] [--clear]
              looma [--config <path>] answer "<question>"
              looma [--config <path>] count [--collection documents|images]
              looma [--config <path>] search "<query>" [--top-k N] [--min-score X] [--collection documents|images]
              looma [--config <path>] clear-cache

            --config defaults to config.json in the current directory.
            """);
        return 0;
    }
}
