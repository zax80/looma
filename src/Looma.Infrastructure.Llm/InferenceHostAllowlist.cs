using System.Net;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// Enforces "no information leaves the system" at the network-locality
/// level: is a given inference endpoint's host one we're allowed to talk
/// to? Pure logic, no I/O — deliberately easy to unit test without a real
/// Ollama instance, unlike the Qdrant integration surface.
/// </summary>
public static class InferenceHostAllowlist
{
    /// <summary>
    /// Checks <paramref name="endpoint"/>'s host against <paramref name="allowedHosts"/>.
    /// Entries containing a <c>/</c> are treated as CIDR ranges (e.g.
    /// <c>10.0.0.0/8</c>) and matched only against literal IP hosts; all
    /// other entries (e.g. <c>localhost</c>, <c>127.0.0.1</c>) are matched
    /// as exact, case-insensitive host strings.
    /// </summary>
    public static bool IsAllowed(Uri endpoint, IReadOnlyList<string> allowedHosts)
    {
        var host = endpoint.Host;

        foreach (var entry in allowedHosts)
        {
            if (entry.Contains('/'))
            {
                if (IPAddress.TryParse(host, out var ip) && IsInCidrRange(ip, entry))
                {
                    return true;
                }
            }
            else if (string.Equals(entry, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInCidrRange(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var networkAddress)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        if (address.AddressFamily != networkAddress.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = networkAddress.GetAddressBytes();

        if (prefixLength < 0 || prefixLength > addressBytes.Length * 8)
        {
            return false;
        }

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits > 0)
        {
            var mask = (byte)~(0xFF >> remainingBits);
            if ((addressBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }
}
