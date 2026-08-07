using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// Makes "just run the CLI" actually work without a manual "go start Ollama
/// first" step: checks whether Ollama is reachable, launches
/// <c>ollama serve</c> if it isn't, waits for it to come up, then pulls
/// whichever configured models aren't installed yet.
///
/// This class itself is deliberately not unit-tested — it's Process/HTTP
/// orchestration with no meaningful behavior outside a real OS process and
/// a real (or absent) Ollama instance. <see cref="OllamaModelCatalog"/> and
/// <see cref="OllamaPullEvent"/> hold the actual logic and are tested
/// directly.
/// </summary>
public sealed class OllamaLifecycleManager : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _baseAddress;
    private readonly Action<string>? _onStatus;
    private readonly Func<string, Task<bool>>? _confirmInstall;
    private readonly HttpClient _httpClient;

    /// <param name="endpoint">Ollama's base endpoint, e.g. http://localhost:11434.</param>
    /// <param name="onStatus">Called with human-readable progress messages as things happen.</param>
    /// <param name="confirmInstall">
    /// Called with a description of the install command this would run
    /// (e.g. "winget install --id Ollama.Ollama ...") when the 'ollama'
    /// executable can't be found at all. Return true to actually run it.
    /// Left null (the default), a missing executable just fails with
    /// instructions — nothing is ever installed without this being supplied
    /// and returning true, and the CLI only supplies it when talking to a
    /// real interactive terminal.
    /// </param>
    public OllamaLifecycleManager(string endpoint, Action<string>? onStatus = null, Func<string, Task<bool>>? confirmInstall = null)
    {
        _baseAddress = new Uri(endpoint);
        _onStatus = onStatus;
        _confirmInstall = confirmInstall;
        _httpClient = new HttpClient { BaseAddress = _baseAddress, Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task EnsureReadyAsync(IReadOnlyList<string> requiredModels, CancellationToken cancellationToken = default)
    {
        if (!await IsReachableAsync(cancellationToken).ConfigureAwait(false))
        {
            Report("Ollama isn't running — starting it...");
            await StartProcessAsync(cancellationToken).ConfigureAwait(false);
            await WaitUntilReachableAsync(cancellationToken).ConfigureAwait(false);
            Report("Ollama is up.");
        }

        await EnsureModelsPulledAsync(requiredModels, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Deliberately not tracking/awaiting this process — `ollama
            // serve` is a long-running server meant to keep running after
            // Looma exits, the same way it would if you'd started it
            // yourself in another terminal.
            Process.Start(BuildServeStartInfo());
        }
        catch (Win32Exception)
        {
            await HandleMissingExecutableAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static ProcessStartInfo BuildServeStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ollama",
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Only takes effect for an Ollama process Looma itself starts — an
        // already-running instance keeps whatever keep_alive it started
        // with. Default is 5 minutes; every CLI invocation is a fresh
        // process, so without this, answering a second question a few
        // minutes after the first pays the full model-load cost again. 30
        // minutes trades some idle memory for that not happening during a
        // normal work session, without holding a multi-GB model in memory
        // indefinitely the way -1 would.
        startInfo.EnvironmentVariables["OLLAMA_KEEP_ALIVE"] = "30m";

        return startInfo;
    }

    private async Task HandleMissingExecutableAsync(CancellationToken cancellationToken)
    {
        if (_confirmInstall is null)
        {
            throw new OllamaLifecycleException(
                "Couldn't find the 'ollama' executable to start it automatically. " +
                $"Install Ollama from {OllamaInstaller.DownloadPageUrl}, make sure it's on PATH, then try again.");
        }

        var installCommand = OllamaInstaller.GetInstallCommand();

        if (installCommand is null)
        {
            // No package-manager-based install available on this platform
            // (or winget/brew themselves aren't found) — offer to at least
            // open the download page rather than silently doing nothing.
            var openBrowser = await _confirmInstall($"open {OllamaInstaller.DownloadPageUrl} in your browser").ConfigureAwait(false);
            if (openBrowser)
            {
                OllamaInstaller.OpenDownloadPageInBrowser();
            }

            throw new OllamaLifecycleException(
                $"Install Ollama from {OllamaInstaller.DownloadPageUrl}, make sure it's on PATH, then try again.");
        }

        var proceed = await _confirmInstall(installCommand.Description).ConfigureAwait(false);
        if (!proceed)
        {
            throw new OllamaLifecycleException(
                "Ollama isn't installed. Install it from " +
                $"{OllamaInstaller.DownloadPageUrl}, or re-run and accept the install prompt, then try again.");
        }

        Report($"Running: {installCommand.Description}");
        var succeeded = await OllamaInstaller.RunAsync(installCommand, cancellationToken).ConfigureAwait(false);
        if (!succeeded)
        {
            throw new OllamaLifecycleException(
                $"'{installCommand.Description}' didn't complete successfully. " +
                $"Try installing manually from {OllamaInstaller.DownloadPageUrl}.");
        }

        Report("Ollama installed. Starting it...");

        try
        {
            Process.Start(BuildServeStartInfo());
        }
        catch (Win32Exception ex)
        {
            // Very common right after a fresh install: PATH was updated by
            // the installer, but this already-running process's environment
            // is a snapshot from before that happened.
            throw new OllamaLifecycleException(
                "Ollama was installed, but this terminal session doesn't see it on PATH yet. " +
                "Close and reopen your terminal, then run this again.", ex);
        }
    }

    private async Task WaitUntilReachableAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsReachableAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }

        throw new OllamaLifecycleException(
            $"Started 'ollama serve' but it didn't become reachable at {_baseAddress} within {StartupTimeout.TotalSeconds:0} seconds.");
    }

    private async Task EnsureModelsPulledAsync(IReadOnlyList<string> requiredModels, CancellationToken cancellationToken)
    {
        var installed = await GetInstalledModelNamesAsync(cancellationToken).ConfigureAwait(false);
        var missing = OllamaModelCatalog.FindMissing(requiredModels, installed);

        foreach (var model in missing)
        {
            Report($"Pulling model '{model}' (this can take a while for larger models)...");
            await PullModelAsync(model, cancellationToken).ConfigureAwait(false);
            Report($"'{model}' ready.");
        }
    }

    private async Task<IReadOnlyList<string>> GetInstalledModelNamesAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("/api/tags", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken).ConfigureAwait(false);
        return tags?.Models?
            .Select(m => m.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList() ?? [];
    }

    private async Task PullModelAsync(string model, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/pull")
        {
            Content = JsonContent.Create(new { model })
        };

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using (var reader = new StreamReader(stream))
        {
            var lastReportedPercent = -10;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>(line);
                if (pullEvent is null)
                {
                    continue;
                }

                if (pullEvent.IsError)
                {
                    throw new OllamaLifecycleException($"Failed to pull '{model}': {pullEvent.Error}");
                }

                if (pullEvent.Total is > 0 && pullEvent.Completed is { } completed)
                {
                    var percent = (int)(completed * 100 / pullEvent.Total.Value);
                    if (percent - lastReportedPercent >= 10)
                    {
                        Report(pullEvent.Describe(model));
                        lastReportedPercent = percent;
                    }
                }
                else
                {
                    Report(pullEvent.Describe(model));
                }

                if (pullEvent.IsSuccess)
                {
                    return;
                }
            }
        }

        throw new OllamaLifecycleException($"'/api/pull' for '{model}' ended without reporting success.");
    }

    private void Report(string message) => _onStatus?.Invoke(message);

    public void Dispose() => _httpClient.Dispose();
}
