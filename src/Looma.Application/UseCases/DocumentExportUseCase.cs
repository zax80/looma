using System.Text;
using Looma.Application.DocumentGeneration;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

public sealed class DocumentExportUseCase : IDocumentExportUseCase
{
    public Task<byte[]> ExportAsync(string title, string content, DocumentExportFormat format, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = format switch
        {
            DocumentExportFormat.Word => DocxDocumentWriter.Write(title, content),
            DocumentExportFormat.Markdown => Encoding.UTF8.GetBytes(BuildMarkdown(title, content)),
            DocumentExportFormat.PlainText => Encoding.UTF8.GetBytes(BuildPlainText(title, content)),
            DocumentExportFormat.Pdf => PdfDocumentWriter.Write(title, content),
            _ => throw new NotSupportedException($"Unsupported export format: {format}")
        };

        return Task.FromResult(bytes);
    }

    private static string BuildMarkdown(string title, string content) =>
        string.IsNullOrWhiteSpace(title) ? content : $"# {title}\n\n{content}";

    private static string BuildPlainText(string title, string content) =>
        string.IsNullOrWhiteSpace(title) ? content : $"{title}\n{new string('=', title.Length)}\n\n{content}";
}
