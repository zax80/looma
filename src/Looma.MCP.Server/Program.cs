using System.Security.Cryptography.X509Certificates;
using Looma.Application;
using Looma.Infrastructure.Llm;
using Looma.Infrastructure.VectorStore.Qdrant;
using Looma.MCP.Server.Auth;
using Microsoft.AspNetCore.Server.Kestrel.Https;
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
/// MCP-client mode are a follow-up. What *is* enforced here, on every
/// single request including tool-listing: API-key auth and Host-header
/// validation, regardless of transport.
///
/// TLS (<c>Mcp:Tls</c>): opt-in, off by default — plain HTTP on localhost
/// remains the default experience for the common single-local-user case,
/// no cert to manage. When <c>Mcp:Tls:Enabled</c> is true and
/// <c>Mcp:Tls:CertificatePath</c> is unset, Kestrel falls back to the
/// standard ASP.NET Core HTTPS developer certificate (the same one
/// `dotnet dev-certs https` manages) for any `https://` endpoint with no
/// certificate explicitly configured — that's a real, if self-signed, TLS
/// handshake, not a bypass; the client machine needs to trust that dev
/// cert (`dotnet dev-certs https --trust`) or the connection fails
/// correctly rather than silently succeeding unencrypted. A real
/// CertificatePath (PFX) is the production-appropriate path once this
/// crosses a network boundary that matters.
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

        // TLS is opt-in (see the class doc comment) — resolved fully before
        // anything starts, same "fail loud at startup, not on first
        // request" discipline as the auth checks above. Three shapes:
        // disabled (default — plain HTTP, nothing to validate); enabled
        // with no CertificatePath (Kestrel falls back to the ASP.NET Core
        // HTTPS dev cert, nothing to load here either); enabled with a
        // CertificatePath (must actually exist and load, or refuse to
        // start rather than silently falling back to the dev cert).
        var tlsEnabled = configuration.GetValue<bool>("Mcp:Tls:Enabled");
        var certificatePath = configuration["Mcp:Tls:CertificatePath"];
        X509Certificate2? tlsCertificate = null;

        if (tlsEnabled && !string.IsNullOrWhiteSpace(certificatePath))
        {
            if (!File.Exists(certificatePath))
            {
                Console.Error.WriteLine($"Mcp:Tls:CertificatePath '{certificatePath}' does not exist.");
                return 1;
            }

            var certPasswordEnvVar = configuration["Mcp:Tls:CertificatePasswordEnvVar"];
            var certPassword = string.IsNullOrWhiteSpace(certPasswordEnvVar)
                ? null
                : Environment.GetEnvironmentVariable(certPasswordEnvVar);

            try
            {
                tlsCertificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certPassword);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Couldn't load Mcp:Tls:CertificatePath '{certificatePath}' as a PFX ({ex.Message}). " +
                    "If it's password-protected, set Mcp:Tls:CertificatePasswordEnvVar to the name of an " +
                    "environment variable holding that password.");
                return 1;
            }
        }

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

        // Only touches the DEFAULT HTTPS certificate Kestrel uses for an
        // `https://` endpoint that doesn't specify its own — when
        // tlsCertificate is null (Enabled but no CertificatePath given),
        // leaving this unset is exactly what makes Kestrel fall back to
        // the ASP.NET Core HTTPS dev cert on its own, so there's nothing
        // to configure in that case.
        if (tlsEnabled && tlsCertificate is not null)
        {
            builder.WebHost.ConfigureKestrel(serverOptions =>
                serverOptions.ConfigureHttpsDefaults(httpsOptions =>
                    httpsOptions.ServerCertificate = tlsCertificate));
        }

        var app = builder.Build();

        if (app.Configuration["urls"] is null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
        {
            app.Urls.Add(tlsEnabled ? "https://localhost:3001" : "http://localhost:3001");
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
        if (tlsEnabled)
        {
            var certSource = tlsCertificate is not null
                ? $"'{certificatePath}'"
                : "the ASP.NET Core HTTPS dev cert (run 'dotnet dev-certs https --trust' on any client machine)";
            Console.WriteLine($"  Transport:    HTTPS, stateless — certificate: {certSource}.");
        }
        else
        {
            Console.WriteLine("  Transport:    HTTP, stateless — TLS is opt-in (Mcp:Tls:Enabled), off by default.");
            Console.WriteLine("                Don't expose this beyond localhost without either enabling it or");
            Console.WriteLine("                putting a TLS-terminating reverse proxy in front of it.");
        }
        Console.WriteLine($"  Auth:         API key required on every request (env var '{apiKeyEnvVar}'),");
        Console.WriteLine($"                plus Host-header validation ({string.Join(", ", allowedHosts)}).");
        Console.WriteLine("  Tools:        looma_index, looma_search, looma_answer, looma_count, looma_clear_cache,");
        Console.WriteLine("                looma_chat, looma_transcribe, looma_caption_image,");
        Console.WriteLine("                looma_extract_document");

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
