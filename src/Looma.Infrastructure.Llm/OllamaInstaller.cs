using System.ComponentModel;
using System.Diagnostics;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// Runs Ollama's own officially-documented install command for the current
/// OS — never anything we invented ourselves. Windows goes through winget
/// (a trusted, already-present OS package manager); macOS through Homebrew
/// if present; Linux through the one-liner from ollama.com/install.sh,
/// which is the same command every official Ollama setup guide publishes.
/// If none of those apply, this falls back to opening the download page in
/// the browser rather than downloading and executing an installer binary
/// itself — that's a deliberate line not to cross without the person
/// driving it themselves.
/// </summary>
public static class OllamaInstaller
{
    public sealed record InstallCommand(string Description, string FileName, string Arguments);

    public static InstallCommand? GetInstallCommand()
    {
        if (OperatingSystem.IsWindows() && IsOnPath("winget"))
        {
            const string args = "install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements";
            return new InstallCommand($"winget {args}", "winget", args);
        }

        if (OperatingSystem.IsMacOS() && IsOnPath("brew"))
        {
            return new InstallCommand("brew install ollama", "brew", "install ollama");
        }

        if (OperatingSystem.IsLinux())
        {
            const string script = "curl -fsSL https://ollama.com/install.sh | sh";
            return new InstallCommand(script, "/bin/sh", $"-c \"{script}\"");
        }

        return null;
    }

    public static string DownloadPageUrl => OperatingSystem.IsWindows() ? "https://ollama.com/download/windows"
        : OperatingSystem.IsMacOS() ? "https://ollama.com/download/mac"
        : "https://ollama.com/download/linux";

    /// <summary>
    /// Runs the install command with the console fully inherited (no
    /// redirected streams) — so the person sees exactly what the installer
    /// prints, and so anything it needs (e.g. a sudo password prompt on
    /// Linux) works normally instead of hanging against a redirected stream
    /// it can't write a prompt to.
    /// </summary>
    public static async Task<bool> RunAsync(InstallCommand command, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new OllamaLifecycleException($"Failed to start install command: {command.Description}");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
    }

    public static void OpenDownloadPageInBrowser()
    {
        Process.Start(new ProcessStartInfo(DownloadPageUrl) { UseShellExecute = true });
    }

    private static bool IsOnPath(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process?.WaitForExit(2000);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
