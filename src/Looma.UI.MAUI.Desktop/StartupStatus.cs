namespace Looma.UI.MAUI.Desktop;

/// <summary>
/// Result of <see cref="MauiProgram"/>'s composition-root setup,
/// registered once as a DI singleton so pages can check it via
/// constructor injection instead of the composition root needing
/// somewhere GUI-specific (a dialog, a crash) to report failures through
/// — unlike Looma.CLI, there's no process exit code a double-clicked
/// app's user would ever see. The chat page (a later milestone) uses
/// this to show a startup error banner instead of a blank screen when
/// config.json is missing, Ollama won't start, or the MCP server can't
/// be reached.
/// </summary>
public sealed class StartupStatus
{
    public bool Success { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<string> Log { get; }

    private StartupStatus(bool success, string? errorMessage, IReadOnlyList<string> log)
    {
        Success = success;
        ErrorMessage = errorMessage;
        Log = log;
    }

    public static StartupStatus Ready(IReadOnlyList<string> log) => new(true, null, log);

    public static StartupStatus Failed(string errorMessage, IReadOnlyList<string> log) => new(false, errorMessage, log);
}
