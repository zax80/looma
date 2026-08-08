using Looma.Application;
using Looma.Application.Configuration;
using Looma.Infrastructure.Llm;
using Looma.Infrastructure.VectorStore.Qdrant;
using Looma.MCP.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using McpClientType = ModelContextProtocol.Client.McpClient;

namespace Looma.UI.MAUI.Desktop;

/// <summary>
/// Composition root for Looma.UI.MAUI.Desktop — same Deployment:Mode
/// branch ("Standalone" vs "McpClient") as Looma.CLI's Program.cs. Per
/// CLAUDE.md rule 1, this is the one file in the project allowed to
/// reference Infrastructure.* or Looma.MCP.Client directly; pages only
/// ever see Looma.Application's use-case interfaces via constructor
/// injection, so the same page code runs unmodified regardless of mode.
///
/// Config discovery differs from the CLI on purpose: a GUI app launched
/// by double-click has no meaningful "current working directory" the way
/// a terminal does, so this reads config.json from
/// <see cref="AppContext.BaseDirectory"/> (next to the built binary)
/// instead. The .csproj copies the repo-root config.json there at build
/// time (see the &lt;None Include="..\..\config.json" .../&gt; item), so
/// there's still one source of truth shared with Looma.CLI and
/// Looma.MCP.Server, not a second copy to keep in sync by hand.
///
/// <see cref="MauiApp.CreateBuilder"/> is synchronous, but connecting
/// (Ollama readiness, or the MCP handshake) is inherently async — this
/// blocks app startup on that work rather than deferring it behind a
/// loading page. That's a deliberate, known shortcut for this milestone
/// (wiring the composition root); revisit once the chat page exists and
/// a loading state has somewhere to render.
///
/// The blocking itself runs via Task.Run(...).GetAwaiter().GetResult(),
/// NOT a bare GetAwaiter().GetResult() on the async method directly —
/// that distinction matters and was hit as a real bug, not a
/// theoretical one: CreateMauiApp() runs on a thread that already has a
/// SynchronizationContext installed by this point, so a bare
/// GetAwaiter().GetResult() deadlocks the moment the awaited chain tries
/// to resume a continuation back on that same (now synchronously
/// blocked) thread. Standalone mode happened not to hit it — its model
/// file checks complete synchronously when the files are already cached
/// locally, so the deadlock-prone code path was never actually
/// exercised — but McpClient mode's real network I/O in
/// LoomaMcpConnection.ConnectAsync hit it every time (hung forever, no
/// window ever appeared). Task.Run moves the whole awaited chain onto a
/// thread-pool thread that has no SynchronizationContext, so its
/// continuations never try to resume on the blocked UI thread at all.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        ConfigureLooma(builder.Services);

        // Resolved through the DI container so its constructor can pull
        // IAnswerUseCase/StartupStatus — see MainPage's own doc comment
        // for how Shell's ContentTemplate binding finds this.
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }

    private static void ConfigureLooma(IServiceCollection services)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var log = new List<string>();

        void OnStatus(string message)
        {
            log.Add(message);
            System.Diagnostics.Debug.WriteLine(message);
        }

        if (!File.Exists(configPath))
        {
            services.AddSingleton(StartupStatus.Failed(
                $"Config file not found: '{configPath}'. The build copies config.json from the repo " +
                "root next to the app — check it exists at the repo root and rebuild.",
                log));
            return;
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
            services.AddSingleton(StartupStatus.Failed($"Failed to load config from '{configPath}': {ex.Message}", log));
            return;
        }

        services.AddSingleton(configuration);

        var deploymentMode = configuration["Deployment:Mode"];
        if (string.IsNullOrWhiteSpace(deploymentMode))
        {
            deploymentMode = "Standalone";
        }

        try
        {
            switch (deploymentMode)
            {
                case "Standalone":
                    Task.Run(() => ConfigureStandaloneAsync(configuration, services, OnStatus, CancellationToken.None))
                        .GetAwaiter().GetResult();
                    break;

                case "McpClient":
                    var client = Task.Run(() => ConfigureMcpClientAsync(configuration, services, OnStatus, CancellationToken.None))
                        .GetAwaiter().GetResult();
                    // Registered so it can eventually be disposed on shutdown.
                    // MAUI doesn't reliably dispose its ServiceProvider on
                    // every platform's app-exit path today — same "not yet
                    // solved" category as Looma.MCP.Client's own documented
                    // lack of reconnect/retry logic. Not a regression from
                    // Looma.CLI (which disposes explicitly in Program.cs);
                    // just not carried over yet.
                    services.AddSingleton(client);
                    break;

                default:
                    services.AddSingleton(StartupStatus.Failed(
                        $"Unknown Deployment:Mode '{deploymentMode}'. Expected \"Standalone\" or \"McpClient\".",
                        log));
                    return;
            }
        }
        catch (Exception ex)
        {
            // Same "fail loudly, once, at startup" spirit as Looma.CLI's
            // catch clauses (OllamaLifecycleException,
            // InferenceEndpointNotAllowedException, the McpClient-mode
            // InvalidOperationException/McpException/HttpRequestException
            // set) — collapsed to one catch-all here because a GUI app has
            // nowhere as direct as a process exit code to report through;
            // the message ends up in StartupStatus for the page to show.
            services.AddSingleton(StartupStatus.Failed(ex.Message, log));
            return;
        }

        services.AddSingleton(StartupStatus.Ready(log));
    }

    /// <summary>
    /// Everything standalone mode needs — mirrors Looma.CLI's
    /// ConfigureStandaloneAsync exactly, except status updates go through
    /// <paramref name="onStatus"/> (captured into StartupStatus.Log)
    /// instead of Console.WriteLine, which nobody would see in a
    /// double-clicked GUI app.
    /// </summary>
    private static async Task ConfigureStandaloneAsync(
        IConfiguration configuration,
        IServiceCollection services,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        await OllamaStartup.EnsureOllamaReadyAsync(
            configuration,
            onStatus: message => onStatus($"[ollama] {message}"),
            confirmInstall: _ => Task.FromResult(false), // no console to prompt on in a GUI app
            cancellationToken);

        try
        {
            await LocalModelFileProvisioner.EnsureImageEmbeddingModelReadyAsync(
                configuration,
                onStatus: message => onStatus($"[clip] {message}"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            onStatus(
                $"[clip] Warning: couldn't auto-provision the CLIP model ({ex.Message}). " +
                "Image indexing will fail until this is resolved; everything else is unaffected.");
        }

        try
        {
            await LocalModelFileProvisioner.EnsureSpeechToTextModelReadyAsync(
                configuration,
                onStatus: message => onStatus($"[whisper] {message}"),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            onStatus(
                $"[whisper] Warning: couldn't auto-provision the Whisper model ({ex.Message}). " +
                "Audio indexing will fail until this is resolved; everything else is unaffected.");
        }

        services.AddQdrantVectorStore(configuration);
        services.AddQdrantAnswerCache(configuration);
        services.AddLoomaChatClient(configuration);
        services.AddLoomaEmbeddingGenerator(configuration);
        services.AddLoomaImageCaptioner(configuration);
        services.AddLoomaImageEmbeddingGenerator(configuration);
        services.AddLoomaAudioTranscriber(configuration);
        services.AddLoomaApplicationUseCases(configuration);
    }

    /// <summary>
    /// McpClient mode: no local Ollama/model provisioning — the remote
    /// Looma.MCP.Server owns that entirely. Mirrors Looma.CLI's
    /// ConfigureMcpClientAsync exactly.
    /// </summary>
    private static async Task<McpClientType> ConfigureMcpClientAsync(
        IConfiguration configuration,
        IServiceCollection services,
        Action<string> onStatus,
        CancellationToken cancellationToken)
    {
        onStatus("[mcp-client] Connecting to remote Looma.MCP.Server...");
        var client = await LoomaMcpConnection.ConnectAsync(configuration, cancellationToken);
        onStatus("[mcp-client] Connected.");

        services.AddOptions<RagOptions>().Bind(configuration.GetSection(RagOptions.SectionName));
        services.AddLoomaMcpClientUseCases(client);

        return client;
    }
}
