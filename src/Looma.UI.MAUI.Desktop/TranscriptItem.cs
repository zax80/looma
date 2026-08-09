using System.ComponentModel;
using System.Runtime.CompilerServices;
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
        set => SetField(ref _text, value);
    }

    private string? _citationsText;
    public string? CitationsText
    {
        get => _citationsText;
        set => SetField(ref _citationsText, value);
    }

    /// <summary>Structured citations, kept alongside CitationsText's rendered form — needed by the "Save answer" action.</summary>
    public IReadOnlyList<DocumentChunk>? Citations { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
