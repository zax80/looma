namespace Looma.MCP.Server.Auth;

/// <summary>
/// Rejects requests with an unrecognized <c>Host</c> header — the SDK's own
/// security guidance for HTTP transport flags this as a required DNS-
/// rebinding defense, since Kestrel doesn't validate <c>Host</c> headers by
/// default. Deliberately a small hand-rolled check (against
/// <c>Mcp:AllowedHosts</c> in config) rather than reaching for an
/// unfamiliar built-in ASP.NET Core host-filtering API — this is easy to
/// get right and easy to verify directly.
/// </summary>
public static class HostAllowList
{
    public static readonly string[] Default = ["localhost", "127.0.0.1", "::1"];

    public static bool IsAllowed(string host, IReadOnlyCollection<string> allowedHosts) =>
        allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
}
