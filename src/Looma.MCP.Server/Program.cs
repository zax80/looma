using Looma.Application;
using Looma.Infrastructure.Llm;
using Looma.Infrastructure.VectorStore.Qdrant;
using Looma.MCP.Server.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.MCP.Server;

/// <summary>
/// Composition root for standalone MCP-server mode. Mirrors Looma.CLI's
/// Program.cs deliberately: this is the one file in this project allowed
/// to reference Looma.Infrastructure.* directly (CLAUDE.md rule 1) —
/// everything under Tools/ depends only on Looma.Application's use-case
/// interfaces.
///
/// Scope for this milestone (per the explicit "Server first" / "HTTP"
/// choice): stand up Looma.MCP.Server only — Looma.MCP.Client and CLI
/// MCP-client mode are a follow-up. Transport is plain HTTP, not TLS — that
/// is a deliberate, flagged deferral (see the startup banner below and
/// docs/mcp-server.md), not a silent omission of the brief's "TLS always"
/// posture. What *is* enforced here, on every single request including
/// tool-listing: API-key auth and Host-header validation.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var (configPath, remainingArgs) = ExtractConfigOption(args);

        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine(
                $"Config file not found: '{configPath}'. Run from a directory containing config.json, " +
                "or pass --config <path>.");
            return 1;
        }

        var builder = WebApplication.CreateBuilder(remainingArgs);

        try
        {
            builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config from '{configPath}': {ex.Message}");
            return 1;
        }

        IConfiguration configuration = builder.Configuration;

        // Auth must be fully resolved and valid *before* anything else
        // starts, including model provisioning — there's no partial-auth
        // mode to fall back into.
        var authMode = configuration["Mcp:Auth:Mode"];
        if (!string.Equals(authMode, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Mcp:Auth:Mode is '{authMode}', but this milestone only supports \"ApiKey\". " +
                "Refusing to start rather than silently falling back to unauthenticated access.");
            return 1;
        }

        var apiKeyEnvVar = configuration["Mcp:Auth:ApiKeyEnvVar"];
        if (string.IsNullOrWhiteSpace(apiKeyEnvVar))
        {
            Console.Error.WriteLine("Mcp:Auth:ApiKeyEnvVar is not set in config.json.");
            return 1;
        }

        var apiKey = Environment.GetEnvironmentVariable(apiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine(
                $"Environment variable '{apiKeyEnvVar}' is not set. Refusing to start an MCP server without " +
                "an API key configured — every connection must be authenticated. Set it and retry, e.g.:\n" +
                $"  PowerShell:  $env:{apiKeyEnvVar} = \"<a long random string>\"\n" +
                $"  bash:        export {apiKeyEnvVar}=\"<a long random string>\"");
            return 1;
        }

        var allowedHosts = configuration.GetSection("Mcp:AllowedHosts").Get<string[]>() ?? HostAllowList.Default;

        try
        {
            await OllamaStartup.EnsureOllamaReadyAsync(
                configuration,
                onStatus: message => Console.WriteLine($"[ollama] {message}"),
                confirmInstall: _ => Task.FromResult(false), // server mode is never interactive
                CancellationToken.None);
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

        try
        {
            await LocalModelFileProvisioner.EnsureImageEmbeddingModelReadyAsync(
                configuration, message => Console.WriteLine($"[clip] {message}"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[clip] Warning: couldn't auto-provision the CLIP model ({ex.Message}). " +
                "looma_index will fail on image files until this is resolved; everything else is unaffected.");
        }

        try
        {
            await LocalModelFileProvisioner.EnsureSpeechToTextModelReadyAsync(
                configuration, message => Console.WriteLine($"[whisper] {message}"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[whisper] Warning: couldn't auto-provision the Whisper model ({ex.Message}). " +
                "looma_index will fail on audio files until this is resolved; everything else is unaffected.");
        }

        builder.Services.AddSingleton(configuration);

        try
        {
            builder.Services.AddQdrantVectorStore(configuration);
            builder.Services.AddQdrantAnswerCache(configuration);
            builder.Services.AddLoomaChatClient(configuration);
            builder.Services.AddLoomaEmbeddingGenerator(configuration);
            builder.Services.AddLoomaImageCaptioner(configuration);
            builder.Services.AddLoomaImageEmbeddingGenerator(configuration);
            builder.Services.AddLoomaAudioTranscriber(configuration);
            builder.Services.AddLoomaApplicationUseCases(configuration);
        }
        catch (InferenceEndpointNotAllowedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly();

        var app = builder.Build();

        if (app.Configuration["urls"] is null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
        {
            app.Urls.Add("http://localhost:3001");
        }

        // Gate every request — including tool-listing — behind Host and
        // API-key checks, before MCP request handling ever runs. Per
        // CLAUDE.md: "must not respond to tool-listing requests from an
        // unauthenticated caller." This runs first, ahead of app.MapMcp(),
        // for literally every request regardless of JSON-RPC method.
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (!HostAllowList.IsAllowed(host, allowedHosts))
            {
                context.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
                await context.Response.WriteAsync($"Unrecognized host '{host}'.");
                return;
            }

            if (!ApiKeyAuthorizer.IsAuthorized(context.Request.Headers, apiKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: missing or invalid API key.");
                return;
            }

            await next();
        });

        app.MapMcp();

        var listeningOn = app.Urls.Count > 0 ? string.Join(", ", app.Urls) : "(default)";

        Console.WriteLine("Looma MCP server");
        Console.WriteLine($"  Listening on: {listeningOn}");
        Console.WriteLine("  Transport:    HTTP, stateless — TLS is deferred to a follow-up milestone.");
        Console.WriteLine("                Don't expose this beyond localhost without a TLS-terminating");
        Console.WriteLine("                reverse proxy in front of it.");
        Console.WriteLine($"  Auth:         API key required on every request (env var '{apiKeyEnvVar}'),");
        Console.WriteLine($"                plus Host-header validation ({string.Join(", ", allowedHosts)}).");
        Console.WriteLine("  Tools:        looma_index, looma_search, looma_answer, looma_count, looma_clear_cache");

        await app.RunAsync();
        return 0;
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
}
