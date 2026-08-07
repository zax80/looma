namespace Looma.Infrastructure.Llm;

/// <summary>Thrown when Ollama can't be reached, started, or fully provisioned with required models.</summary>
public sealed class OllamaLifecycleException : Exception
{
    public OllamaLifecycleException(string message) : base(message)
    {
    }

    public OllamaLifecycleException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
