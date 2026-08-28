using Looma.CLI.Setup;

namespace Looma.CLI.Commands;

/// <summary>
/// Deliberately doesn't take an <c>IServiceProvider</c> like every other
/// command here — <c>setup</c> runs *before* config.json necessarily
/// exists, so there's nothing to build a DI container from yet. See
/// <c>Program.Main</c>, which special-cases this command ahead of the
/// "config file must exist" check.
/// </summary>
public static class SetupCommand
{
    public static Task<int> RunAsync(string configPath, string[] args)
    {
        string? domain = null;
        string? location = null;
        string? mode = null;
        string? mcpEndpoint = null;
        bool? horizontalScale = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--domain":
                    domain = args[i + 1];
                    break;
                case "--location":
                    location = args[i + 1];
                    break;
                case "--mode":
                    mode = args[i + 1];
                    break;
                case "--mcp-endpoint":
                    mcpEndpoint = args[i + 1];
                    break;
                case "--scale":
                    if (!bool.TryParse(args[i + 1], out var scaleValue))
                    {
                        Console.Error.WriteLine($"--scale expects 'true' or 'false', got '{args[i + 1]}'.");
                        return Task.FromResult(1);
                    }

                    horizontalScale = scaleValue;
                    break;
            }
        }

        return SetupWizard.RunAsync(configPath, domain, location, mode, mcpEndpoint, horizontalScale);
    }
}
