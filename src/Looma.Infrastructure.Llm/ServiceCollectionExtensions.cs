using System.ClientModel;
using Looma.Core.Abstractions;
using Looma.Infrastructure.Llm.Audio;
using Looma.Infrastructure.Llm.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// Wires up chat + embedding + vision (captioning and CLIP) + speech-to-text
/// (Whisper) against local Ollama / ONNX Runtime / whisper.cpp.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IChatClient"/> against <c>Models.BaseModel</c>.
    /// Throws <see cref="InferenceEndpointNotAllowedException"/> immediately
    /// (not lazily on first call) if that endpoint's host isn't allowlisted.
    /// </summary>
    public static IServiceCollection AddLoomaChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var (llmOptions, securityOptions) = BindOptions(configuration);
        ValidateEndpoint(llmOptions.BaseModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.BaseModel)}");

        services.AddSingleton<IChatClient>(_ =>
        {
            IChatClient client = CreateOpenAiClient(llmOptions.BaseModel.Endpoint, llmOptions.BaseModel.TimeoutSeconds)
                .GetChatClient(llmOptions.BaseModel.Model)
                .AsIChatClient();

            return llmOptions.BaseModel.DisableThinking ? new ReasoningEffortChatClient(client) : client;
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IEmbeddingGenerator{String, Embedding}"/> against
    /// <c>Models.EmbeddingModel</c>. Same fail-loudly-at-registration behavior
    /// as <see cref="AddLoomaChatClient"/>.
    /// </summary>
    public static IServiceCollection AddLoomaEmbeddingGenerator(this IServiceCollection services, IConfiguration configuration)
    {
        var (llmOptions, securityOptions) = BindOptions(configuration);
        ValidateEndpoint(llmOptions.EmbeddingModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.EmbeddingModel)}");

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            CreateOpenAiClient(llmOptions.EmbeddingModel.Endpoint, llmOptions.EmbeddingModel.TimeoutSeconds)
                .GetEmbeddingClient(llmOptions.EmbeddingModel.Model)
                .AsIEmbeddingGenerator());

        return services;
    }

    /// <summary>
    /// The DI key <see cref="OllamaImageCaptioner"/> resolves its
    /// <see cref="IChatClient"/> under. Keyed rather than a second
    /// unkeyed registration — <c>Models.BaseModel</c> and
    /// <c>Models.VisionModel</c> can be different models/endpoints, and an
    /// unkeyed second <c>AddSingleton&lt;IChatClient&gt;</c> would just
    /// overwrite/collide with the chat client's own registration.
    /// </summary>
    public const string VisionChatClientKey = "vision";

    /// <summary>
    /// Registers <see cref="IImageCaptioner"/> (captioning + OCR) against
    /// <c>Models.VisionModel</c>. Same fail-loudly-at-registration endpoint
    /// check as the chat/embedding registrations.
    /// </summary>
    public static IServiceCollection AddLoomaImageCaptioner(this IServiceCollection services, IConfiguration configuration)
    {
        var (llmOptions, securityOptions) = BindOptions(configuration);
        ValidateEndpoint(llmOptions.VisionModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.VisionModel)}");

        services.AddKeyedSingleton<IChatClient>(VisionChatClientKey, (_, _) =>
            CreateOpenAiClient(llmOptions.VisionModel.Endpoint, llmOptions.VisionModel.TimeoutSeconds)
                .GetChatClient(llmOptions.VisionModel.Model)
                .AsIChatClient());

        services.AddSingleton<IImageCaptioner>(sp =>
            new OllamaImageCaptioner(sp.GetRequiredKeyedService<IChatClient>(VisionChatClientKey)));

        return services;
    }

    /// <summary>
    /// Registers <see cref="IImageEmbeddingGenerator"/> (CLIP) against
    /// <c>Models.ImageEmbeddingModel</c>. No endpoint/locality check here —
    /// unlike Ollama, this is a local in-process ONNX Runtime session, not
    /// an HTTP call, so there's no network endpoint to validate against
    /// <c>Security.AllowedInferenceHosts</c> in the first place.
    /// </summary>
    public static IServiceCollection AddLoomaImageEmbeddingGenerator(this IServiceCollection services, IConfiguration configuration)
    {
        var (llmOptions, _) = BindOptions(configuration);
        var modelPath = llmOptions.ImageEmbeddingModel.ModelPath
            ?? throw new InvalidOperationException(
                $"'{LlmOptions.SectionName}:{nameof(LlmOptions.ImageEmbeddingModel)}:ModelPath' is not set in config.json.");

        services.AddSingleton<IImageEmbeddingGenerator>(_ => new OnnxClipImageEmbeddingGenerator(modelPath));

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAudioTranscriber"/> (Whisper) against
    /// <c>Models.SpeechToTextModel</c>. No endpoint/locality check here for
    /// the same reason as <see cref="AddLoomaImageEmbeddingGenerator"/> —
    /// this is a local whisper.cpp process via Whisper.net, not an HTTP call.
    /// </summary>
    public static IServiceCollection AddLoomaAudioTranscriber(this IServiceCollection services, IConfiguration configuration)
    {
        var (llmOptions, _) = BindOptions(configuration);
        var modelPath = llmOptions.SpeechToTextModel.ModelPath
            ?? throw new InvalidOperationException(
                $"'{LlmOptions.SectionName}:{nameof(LlmOptions.SpeechToTextModel)}:ModelPath' is not set in config.json.");

        services.AddSingleton<IAudioTranscriber>(_ => new WhisperAudioTranscriber(modelPath));

        return services;
    }

    internal static (LlmOptions Llm, SecurityOptions Security) BindOptions(IConfiguration configuration)
    {
        var llmOptions = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var securityOptions = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
        return (llmOptions, securityOptions);
    }

    internal static void ValidateEndpoint(string endpoint, SecurityOptions security, string configPath)
    {
        if (!security.BlockNonLocalEndpoints)
        {
            return;
        }

        var uri = new Uri(endpoint);
        if (!InferenceHostAllowlist.IsAllowed(uri, security.AllowedInferenceHosts))
        {
            throw new InferenceEndpointNotAllowedException(
                $"Refusing to start: '{configPath}' is configured to '{endpoint}', whose host " +
                $"'{uri.Host}' is not in Security.AllowedInferenceHosts. This check exists to " +
                "guarantee no inference traffic leaves the system undetected — fix config.json " +
                "rather than working around this.");
        }
    }

    private static OpenAIClient CreateOpenAiClient(string ollamaEndpoint, int timeoutSeconds)
    {
        var openAiCompatibleBase = new Uri($"{ollamaEndpoint.TrimEnd('/')}/v1");

        // Ollama's OpenAI-compatible surface ignores the API key entirely,
        // but the OpenAI SDK requires a non-empty credential to construct a
        // client — this value is never sent anywhere that checks it.
        //
        // NetworkTimeout overrides the SDK's own 100-second default (see
        // ModelEndpointOptions.TimeoutSeconds for why that's too short here)
        // — without this, a real run's vision-captioning call retried 4
        // times and still failed purely from timing out, never from an
        // actual error.
        return new OpenAIClient(
            new ApiKeyCredential("ollama-local"),
            new OpenAIClientOptions
            {
                Endpoint = openAiCompatibleBase,
                NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds)
            });
    }
}
