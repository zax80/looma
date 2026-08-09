using System.Collections.ObjectModel;
using System.Text;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Audio;

namespace Looma.UI.MAUI.Desktop;

/// <summary>
/// Chat-style view: a session sidebar + a multi-turn transcript, backed by
/// <see cref="IChatUseCase"/>. Constructor-injected via the DI container
/// (registered AddTransient&lt;MainPage&gt; in MauiProgram.cs) — Shell's
/// ContentTemplate="{DataTemplate local:MainPage}" resolves it through the
/// app's IServiceProvider.
///
/// <see cref="IChatUseCase"/>/<see cref="ISavedAnswerUseCase"/> are
/// resolved from <paramref name="services"/> (nullable) rather than
/// injected directly for two reasons: they're only registered when
/// MauiProgram's composition root actually succeeded (see
/// <see cref="StartupStatus"/>), and — separately — IChatUseCase isn't
/// registered at all in McpClient mode yet (no looma_chat MCP tool
/// exists). The page needs to run and show a clear message in both cases,
/// not just when config is missing.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly IChatUseCase? _chatUseCase;
    private readonly ISavedAnswerUseCase? _savedAnswerUseCase;
    private readonly ITranscriptionUseCase? _transcriptionUseCase;
    private readonly IImageCaptionUseCase? _imageCaptionUseCase;
    private readonly IAudioRecorder? _audioRecorder;
    private readonly ObservableCollection<TranscriptItem> _transcript = [];
    private readonly ObservableCollection<ChatSessionSummary> _sessions = [];

    private string? _currentSessionId;
    private CancellationTokenSource? _pendingSendCts;
    private bool _initialized;

    private string? _pendingImageFileName;
    private string? _pendingImageCaption;
    private Task? _micStartTask;

    public MainPage(IServiceProvider services, StartupStatus startupStatus)
    {
        InitializeComponent();

        TranscriptView.ItemsSource = _transcript;
        SessionsView.ItemsSource = _sessions;

        _chatUseCase = services.GetService<IChatUseCase>();
        _savedAnswerUseCase = services.GetService<ISavedAnswerUseCase>();
        _transcriptionUseCase = services.GetService<ITranscriptionUseCase>();
        _imageCaptionUseCase = services.GetService<IImageCaptionUseCase>();
        _audioRecorder = services.GetService<IAudioManager>()?.CreateRecorder();

        // No point showing controls that can't do anything — same
        // registered-only-in-Standalone-mode gap as IChatUseCase itself
        // (see docs/config-reference.md's ChatHistory section).
        MicButton.IsVisible = _transcriptionUseCase is not null && _audioRecorder is not null;
        AttachButton.IsVisible = _imageCaptionUseCase is not null;

        if (!startupStatus.Success)
        {
            StartupErrorLabel.Text = startupStatus.ErrorMessage;
            StartupErrorLabel.IsVisible = true;
            SetInputEnabled(false);
            return;
        }

        if (_chatUseCase is null)
        {
            ChatUnavailableLabel.Text =
                "Multi-turn chat isn't available yet when running against a remote Looma.MCP.Server " +
                "(McpClient mode) — this needs a new server-side tool that doesn't exist yet. " +
                "Switch config.json's Deployment:Mode to \"Standalone\" to use chat.";
            ChatUnavailableLabel.IsVisible = true;
            SetInputEnabled(false);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized || _chatUseCase is null)
        {
            return;
        }
        _initialized = true;

        await ReloadSessionsAsync();

        if (_sessions.Count > 0)
        {
            await OpenSessionAsync(_sessions[0].Id);
        }
        else
        {
            await StartNewSessionAsync();
        }
    }

    private async void OnNewChatClicked(object? sender, EventArgs e) => await StartNewSessionAsync();

    private async void OnSessionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ChatSessionSummary summary)
        {
            await OpenSessionAsync(summary.Id);
        }
    }

    private async void OnDeleteSessionClicked(object? sender, EventArgs e)
    {
        if (_chatUseCase is null || sender is not Button { CommandParameter: string sessionId })
        {
            return;
        }

        var confirmed = await DisplayAlertAsync("Delete chat", "Delete this chat? This can't be undone.", "Delete", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await _chatUseCase.DeleteSessionAsync(sessionId);

        if (_currentSessionId == sessionId)
        {
            _currentSessionId = null;
            _transcript.Clear();
        }

        await ReloadSessionsAsync();

        if (_currentSessionId is null)
        {
            if (_sessions.Count > 0)
            {
                await OpenSessionAsync(_sessions[0].Id);
            }
            else
            {
                await StartNewSessionAsync();
            }
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var typedText = QuestionEntry.Text?.Trim() ?? string.Empty;
        var hasPendingImage = _pendingImageCaption is not null;
        if ((string.IsNullOrWhiteSpace(typedText) && !hasPendingImage) || _chatUseCase is null)
        {
            return;
        }

        // An image with no typed question still needs something to embed
        // for retrieval and to persist as the turn's question text.
        if (string.IsNullOrWhiteSpace(typedText) && hasPendingImage)
        {
            typedText = "What can you tell me about the attached image?";
        }

        if (_currentSessionId is null)
        {
            await StartNewSessionAsync();
        }
        var sessionId = _currentSessionId!;

        // A new message supersedes whatever the previous one was still streaming.
        _pendingSendCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingSendCts = cts;

        // The message text stays clean either way — it's what gets embedded
        // for retrieval AND what gets persisted to the session, so a
        // reloaded session shows exactly what was typed. The image caption
        // travels as ChatUseCase's separate attachmentContext parameter
        // instead of being folded into the question text: ChatUseCase's
        // system prompt only allows answering from the "Context:" block,
        // so a caption smuggled into the question would just get refused
        // by that same grounding rule — see ChatUseCase.BuildPrompt.
        var attachmentContext = hasPendingImage ? _pendingImageCaption : null;
        var attachedFileName = _pendingImageFileName;

        // Shown in the live transcript bubble only — not persisted (the
        // session stores typedText alone), so reopening this session later
        // won't show the 📎 decoration for this turn. A real per-message
        // attachment record would need ChatMessageEntry to carry one;
        // not built this round.
        var displayText = hasPendingImage ? $"📎 {attachedFileName}\n{typedText}" : typedText;

        QuestionEntry.Text = string.Empty;
        _pendingImageFileName = null;
        _pendingImageCaption = null;
        PendingAttachmentRow.IsVisible = false;
        SetInputEnabled(false);

        var userItem = new TranscriptItem { Role = ChatMessageRole.User, Text = displayText };
        var assistantItem = new TranscriptItem { Role = ChatMessageRole.Assistant, Text = string.Empty };
        _transcript.Add(userItem);
        _transcript.Add(assistantItem);
        ScrollToEnd();

        var answerSoFar = new StringBuilder();

        try
        {
            await foreach (var token in _chatUseCase.SendMessageAsync(sessionId, typedText, attachmentContext, cts.Token))
            {
                if (!token.IsFinal)
                {
                    answerSoFar.Append(token.Text);
                    var partial = answerSoFar.ToString();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistantItem.Text = partial;
                        ScrollToEnd();
                    });
                    continue;
                }

                if (token.Citations is { Count: > 0 } citations)
                {
                    var citationsText = FormatCitations(citations);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistantItem.Citations = citations;
                        assistantItem.CitationsText = citationsText;
                    });
                }
            }

            // The session's title may have just been derived from this
            // being its first message, and its ordering (newest-updated
            // first) may have changed — refresh the sidebar to reflect it.
            await ReloadSessionsAsync();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer message — nothing to show.
        }
        catch (Exception ex)
        {
            var errorText = $"Error: {ex.Message}";
            MainThread.BeginInvokeOnMainThread(() => assistantItem.Text = errorText);
        }
        finally
        {
            if (_pendingSendCts == cts)
            {
                MainThread.BeginInvokeOnMainThread(() => SetInputEnabled(true));
            }
        }
    }

    private async void OnSaveAnswerClicked(object? sender, EventArgs e)
    {
        if (_savedAnswerUseCase is null)
        {
            return;
        }

        // Saves the most recent answer only — saving an arbitrary earlier
        // turn from the same session would need a per-message action
        // (CollectionView item button + command), not built this round.
        var lastAssistantItem = _transcript.LastOrDefault(t => t.Role == ChatMessageRole.Assistant && !string.IsNullOrEmpty(t.Text));
        var lastUserItem = _transcript.LastOrDefault(t => t.Role == ChatMessageRole.User);
        if (lastAssistantItem is null || lastUserItem is null)
        {
            await DisplayAlertAsync("Nothing to save", "Ask a question first.", "OK");
            return;
        }

        var title = await DisplayPromptAsync("Save answer", "Title for this saved answer:", initialValue: lastUserItem.Text);
        if (title is null)
        {
            return; // cancelled
        }

        await _savedAnswerUseCase.SaveAsync(
            title,
            lastUserItem.Text,
            lastAssistantItem.Text,
            lastAssistantItem.Citations ?? [],
            _currentSessionId);

        await DisplayAlertAsync("Saved", "Answer saved.", "OK");
    }

    private async void OnViewSavedAnswersClicked(object? sender, EventArgs e)
    {
        if (_savedAnswerUseCase is null)
        {
            return;
        }

        var answers = await _savedAnswerUseCase.ListAsync();
        if (answers.Count == 0)
        {
            await DisplayAlertAsync("Saved answers", "Nothing saved yet.", "OK");
            return;
        }

        var titles = answers.Select(a => a.Title).ToArray();
        var chosen = await DisplayActionSheetAsync("Saved answers", "Cancel", "Delete...", titles);
        if (chosen is null || chosen == "Cancel")
        {
            return;
        }

        if (chosen == "Delete...")
        {
            var toDelete = await DisplayActionSheetAsync("Delete which?", "Cancel", null, titles);
            var match = answers.FirstOrDefault(a => a.Title == toDelete);
            if (match is not null)
            {
                await _savedAnswerUseCase.DeleteAsync(match.Id);
            }
            return;
        }

        var selected = answers.FirstOrDefault(a => a.Title == chosen);
        if (selected is not null)
        {
            await DisplayAlertAsync(selected.Title, $"Q: {selected.Question}\n\nA: {selected.AnswerText}", "Close");
        }
    }

    private async Task ReloadSessionsAsync()
    {
        if (_chatUseCase is null)
        {
            return;
        }

        var summaries = await _chatUseCase.ListSessionsAsync();
        _sessions.Clear();
        foreach (var summary in summaries)
        {
            _sessions.Add(summary);
        }
    }

    private async Task StartNewSessionAsync()
    {
        if (_chatUseCase is null)
        {
            return;
        }

        var session = await _chatUseCase.StartSessionAsync();
        _currentSessionId = session.Id;
        _transcript.Clear();
        await ReloadSessionsAsync();
    }

    private async Task OpenSessionAsync(string sessionId)
    {
        if (_chatUseCase is null)
        {
            return;
        }

        var session = await _chatUseCase.GetSessionAsync(sessionId);
        if (session is null)
        {
            return;
        }

        _currentSessionId = session.Id;
        _transcript.Clear();
        foreach (var entry in session.Messages)
        {
            _transcript.Add(ToTranscriptItem(entry));
        }
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_transcript.Count > 0)
        {
            TranscriptView.ScrollTo(_transcript[^1], position: ScrollToPosition.End, animate: false);
        }
    }

    private void OnMicPressed(object? sender, EventArgs e)
    {
        if (_audioRecorder is null)
        {
            return;
        }

        // Not awaited here — stored so OnMicReleased can wait for genuine
        // completion before calling StopAsync(). A quick press-release can
        // otherwise race MediaCapture's async initialization on Windows:
        // IAudioRecorder.IsRecording flips true the moment the underlying
        // MediaCapture object is constructed, before its own
        // InitializeAsync() has actually finished — StopAsync() called in
        // that window throws "This object needs to be initialized before
        // the requested operation can be carried out" (hit in real
        // testing, not theoretical).
        _micStartTask = StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        try
        {
            await _audioRecorder!.StartAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Microphone error", ex.Message, "OK");
            throw;
        }
    }

    private async void OnMicReleased(object? sender, EventArgs e)
    {
        if (_audioRecorder is null || _transcriptionUseCase is null)
        {
            return;
        }

        var startTask = _micStartTask;
        _micStartTask = null;

        if (startTask is not null)
        {
            try
            {
                await startTask; // wait for real init before stopping
            }
            catch
            {
                return; // already reported by StartRecordingAsync
            }
        }

        if (!_audioRecorder.IsRecording)
        {
            return;
        }

        string? filePath = null;
        try
        {
            var audioSource = await _audioRecorder.StopAsync();
            if (audioSource is not FileAudioSource fileSource)
            {
                return;
            }

            filePath = fileSource.GetFilePath();

            await using var stream = fileSource.GetAudioStream();
            var transcribedText = await _transcriptionUseCase.TranscribeAsync(stream);

            if (!string.IsNullOrWhiteSpace(transcribedText))
            {
                QuestionEntry.Text = string.IsNullOrWhiteSpace(QuestionEntry.Text)
                    ? transcribedText
                    : $"{QuestionEntry.Text} {transcribedText}";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Transcription error", ex.Message, "OK");
        }
        finally
        {
            // Plugin.Maui.Audio doesn't clean up StartAsync()'s generated file itself.
            if (filePath is not null)
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private async void OnAttachImageClicked(object? sender, EventArgs e)
    {
        if (_imageCaptionUseCase is null)
        {
            return;
        }

        FileResult? photo;
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });
            photo = photos?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't open picker", ex.Message, "OK");
            return;
        }

        if (photo is null)
        {
            return; // cancelled
        }

        try
        {
            await using var stream = await photo.OpenReadAsync();
            var result = await _imageCaptionUseCase.CaptionAsync(stream);

            _pendingImageFileName = photo.FileName;
            _pendingImageCaption = result.OcrText is { Length: > 0 }
                ? $"{result.Caption} (text in image: {result.OcrText})"
                : result.Caption;

            PendingAttachmentLabel.Text = $"📎 {photo.FileName}: {_pendingImageCaption}";
            PendingAttachmentRow.IsVisible = true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't analyze image", ex.Message, "OK");
        }
    }

    private void OnRemovePendingAttachmentClicked(object? sender, EventArgs e)
    {
        _pendingImageFileName = null;
        _pendingImageCaption = null;
        PendingAttachmentRow.IsVisible = false;
    }

    private void SetInputEnabled(bool enabled)
    {
        QuestionEntry.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        MicButton.IsEnabled = enabled;
        AttachButton.IsEnabled = enabled;
    }

    private static TranscriptItem ToTranscriptItem(ChatMessageEntry entry) => new()
    {
        Role = entry.Role,
        Text = entry.Text,
        Citations = entry.Citations,
        CitationsText = entry.Citations is { Count: > 0 } citations ? FormatCitations(citations) : null
    };

    private static string FormatCitations(IReadOnlyList<DocumentChunk> citations)
    {
        var sb = new StringBuilder("Sources: ");
        for (var i = 0; i < citations.Count; i++)
        {
            if (i > 0)
            {
                sb.Append("; ");
            }

            sb.Append('[').Append(i + 1).Append("] ").Append(citations[i].SourceId)
                .Append(" (lines ").Append(citations[i].Metadata.StartLine).Append('-').Append(citations[i].Metadata.EndLine).Append(')');
        }

        return sb.ToString();
    }
}
