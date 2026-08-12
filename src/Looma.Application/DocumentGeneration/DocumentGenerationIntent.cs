using Looma.Application.UseCases;
using Looma.Core.Entities;

namespace Looma.Application.DocumentGeneration;

/// <summary>Result of <see cref="DocumentGenerationIntentDetector.Detect"/> — non-null means "offer a document export for this turn".</summary>
public sealed record DocumentGenerationIntent
{
    /// <summary>Format to default the export button to.</summary>
    public required DocumentExportFormat Format { get; init; }
}
