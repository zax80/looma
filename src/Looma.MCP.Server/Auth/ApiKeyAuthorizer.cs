using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Looma.MCP.Server.Auth;

/// <summary>
/// Pure, testable API-key check used by the auth middleware in Program.cs.
/// Split out from Program.cs specifically so it can be unit tested without
/// spinning up Kestrel — Program.cs itself is the composition root and
/// deliberately kept thin.
///
/// Accepts the key via <c>Authorization: Bearer &lt;key&gt;</c> (checked
/// first) or <c>X-Api-Key: &lt;key&gt;</c> (fallback, for MCP clients that
/// don't expose a way to set a custom Authorization header). Comparison is
/// fixed-time to avoid leaking key length/content via response timing.
/// </summary>
public static class ApiKeyAuthorizer
{
    private const string BearerPrefix = "Bearer ";

    public static bool IsAuthorized(IHeaderDictionary headers, string expectedApiKey)
    {
        var providedKey = ExtractApiKey(headers);
        if (string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        return FixedTimeEquals(expectedApiKey, providedKey);
    }

    internal static string? ExtractApiKey(IHeaderDictionary headers)
    {
        if (headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = value[BearerPrefix.Length..].Trim();
                if (key.Length > 0)
                {
                    return key;
                }
            }
        }

        if (headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            var value = apiKeyHeader.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    internal static bool FixedTimeEquals(string expected, string provided)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        // Differing lengths already aren't a match; comparing only up to a
        // common fixed-length buffer would otherwise require padding to
        // avoid a length-based timing signal. Since a length mismatch alone
        // reveals nothing usable about the actual key content, short-
        // circuiting here doesn't meaningfully weaken the fixed-time
        // comparison's purpose.
        return expectedBytes.Length == providedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
