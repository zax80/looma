using System.ComponentModel;
using System.Runtime.CompilerServices;
using Looma.Application.DocumentGeneration;
using Looma.Core.Entities;

namespace Looma.UI.MAUI.Desktop;

/// <summary>
/// One bubble in the chat transcript CollectionView.
/// <see cref="INotifyPropertyChanged"/> (not a record) specifically so the
/// assistant's <see cref="Text"/> can update in place as tokens stream in,
/// without replacing/reloading the whole ObservableCollection on every
/// token.
/// </summary>
public sealed class TranscriptItem : INotifyPropertyChanged
{
    public required ChatMessageRole Role { get; init; }

    public string RoleLabel => Role == ChatMessageRole.User ? "You" : "Looma";

    private string _text = string.Empty;
    public string Text
    {
        get => _text;
        set
        {
            SetField(ref _text, value);
            OnPropertyChanged(nameof(HasText));
        }
    }

    /// <summary>True once any answer text has arrived — used to switch off the "thinking" indicator.</summary>
    public bool HasText => _text.Length > 0;

    private bool _isGenerating;

    /// <summary>
    /// True from the moment an assistant bubble is created until its first
    /// token arrives (or it fails) — MainPage shows a "thinking" indicator
    /// while this is true instead of an empty bubble, so waiting on
    /// retrieval + the model's first token doesn't look like nothing is
    /// happening.
    /// </summary>
    public bool IsGenerating
    {
        get => _isGenerating;
        set => SetField(ref _isGenerating, value);
    }

    private string? _citationsText;
    public string? CitationsText
    {
        get => _citationsText;
        set => SetField(ref _citationsText, value);
    }

    /// <summary>Structured citations, kept alongside CitationsText's rendered form — needed by the "Save answer" action.</summary>
    public IReadOnlyList<DocumentChunk>? Citations { get; set; }

    /// <summary>The user question this answer responds to — used as the export/save title, and by <see cref="DocumentGenerationIntentDetector"/>.</summary>
    public string? SourceQuestion { get; set; }

    private DocumentGenerationIntent? _exportIntent;

    /// <summary>
    /// Non-null when <see cref="DocumentGenerationIntentDetector"/> thinks
    /// <see cref="SourceQuestion"/> was asking for a generated document —
    /// MainPage shows an "Export as..." button on this bubble when set.
    /// </summary>
    public DocumentGenerationIntent? ExportIntent
    {
        get => _exportIntent;
        set
        {
            SetField(ref _exportIntent, value);
            OnPropertyChanged(nameof(ShowExportButton));
            OnPropertyChanged(nameof(ExportButtonText));
        }
    }

    public bool ShowExportButton => _exportIntent is not null;

    public string ExportButtonText => _exportIntent switch
    {
        { PdfRequestedButUnsupported: true } => "Export as .docx (PDF not supported yet)",
        { Format: DocumentExportFormat.Markdown } => "Export as .md",
        { Format: DocumentExportFormat.PlainText } => "Export as .txt",
        _ => "Export as .docx"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
