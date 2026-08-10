using Looma.Core.Entities;

namespace Looma.Application.DocumentGeneration;

public static class DocumentExportFormatExtensions
{
    /// <summary>File extension (with leading dot) a UI should suggest when saving this format.</summary>
    public static string FileExtension(this DocumentExportFormat format) => format switch
    {
        DocumentExportFormat.Word => ".docx",
        DocumentExportFormat.Markdown => ".md",
        DocumentExportFormat.PlainText => ".txt",
        _ => throw new NotSupportedException($"Unsupported export format: {format}")
    };
}
