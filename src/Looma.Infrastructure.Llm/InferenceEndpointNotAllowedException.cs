namespace Looma.Infrastructure.Llm;

/// <summary>
/// Thrown at DI-registration time when a configured inference endpoint's
/// host isn't in <c>Security.AllowedInferenceHosts</c>. Deliberately thrown
/// during service registration, not lazily on first request — CLAUDE.md
/// requires a misconfiguration to fail loudly at startup, not silently
/// phone home on the first chat call.
/// </summary>
public sealed class InferenceEndpointNotAllowedException : Exception
{
    public InferenceEndpointNotAllowedException(string message) : base(message)
    {
    }
}
