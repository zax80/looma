using Looma.Application.UseCases;
using Looma.Core.Entities;

namespace Looma.Application.DocumentGeneration;

/// <summary>Result of <see cref="DocumentGenerationIntentDetector.Detect"/> — non-null means "offer a document export for this turn".</summary>
public sealed record DocumentGenerationIntent
{
    /// <summary>Format to default the export button to.</summary>
    public required DocumentExportFormat Format { get; init; }

    /// <summary>
    /// The user asked for PDF specifically, which isn't implemented — see
    /// <see cref="DocumentExportUseCase"/>. <see cref="Format"/> falls back
    /// to <see cref="DocumentExportFormat.Word"/> in this case; a caller
    /// should surface this flag (e.g. a note next to the export button)
    /// rather than silently substituting formats.
    /// </summary>
    public bool PdfRequestedButUnsupported { get; init; }
}
