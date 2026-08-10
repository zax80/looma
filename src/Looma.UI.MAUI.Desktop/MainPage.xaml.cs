using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Looma.Application.DocumentGeneration;
using Looma.Application.Extraction;
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
/// injected directly because they're only registered once MauiProgram's
/// composition root actually succeeded (see <see cref="StartupStatus"/>)
/// — the page needs to run and show a clear message even when config is
/// missing/invalid. Every use case here — chat, saved answers, voice
/// input, image-attach, and document-attach — is available in both
/// Standalone and McpClient mode (McpClient mode via the corresponding
/// <c>Looma.MCP.Client.Remote*UseCase</c> adapters and MCP tools; see
/// docs/mcp-server.md). Document EXPORT (<see cref="IDocumentExportUseCase"/>)
/// is the one exception worth calling out — not because it's
/// mode-specific, but because it isn't an MCP concern at all: it's pure
/// local formatting of an already-generated answer, registered directly
/// in both modes with no remote adapter needed.
/// </summary>
public partial class MainPage : ContentPage
{
    /// <summary>
    /// Cap on how much of an attached document's extracted text gets sent
    /// as attachmentContext — this is "ask about it live" (see
    /// IDocumentExtractionUseCase's doc comment), not indexing, so there's
    /// no chunking/retrieval to keep it within the model's context window
    /// (ContextSize in config.json — 8192 tokens for the default
    /// BaseModel). A full report could easily blow past that on its own;
    /// truncating is a blunt but simple safeguard. A smarter fix (only
    /// send the most relevant excerpt) would need embedding + retrieval
    /// over the attachment, at which point it's arguably just indexing —
    /// not built this round.
    /// </summary>
    private const int MaxAttachedDocumentChars = 6000;

    private readonly IChatUseCase? _chatUseCase;
    private readonly ISavedAnswerUseCase? _savedAnswerUseCase;
    private readonly ITranscriptionUseCase? _transcriptionUseCase;
    private readonly IImageCaptionUseCase? _imageCaptionUseCase;
    private readonly IDocumentExtractionUseCase? _documentExtractionUseCase;
    private readonly IDocumentExportUseCase? _documentExportUseCase;
    private readonly IAudioRecorder? _audioRecorder;
    private readonly ObservableCollection<TranscriptItem> _transcript = [];
    private readonly ObservableCollection<ChatSessionSummary> _sessions = [];

    private string? _currentSessionId;
    private CancellationTokenSource? _pendingSendCts;
    private bool _initialized;

    // Set by either OnAttachClicked's "Photo" or "Document" branch —
    // whichever ran last wins, same as before when only images existed.
    // _pendingAttachmentContext is what actually travels to ChatUseCase as
    // attachmentContext; _pendingAttachmentLabel is just the 📎 filename
    // shown in the pending-attachment row and the live transcript bubble.
    private string? _pendingAttachmentLabel;
    private string? _pendingAttachmentContext;
    private Task? _micStartTask;

    public MainPage(IServiceProvider services, StartupStatus startupStatus)
    {
        InitializeComponent();

        BindableLayout.SetItemsSource(SessionsStack, _sessions);
        RenderTranscript();

        _chatUseCase = services.GetService<IChatUseCase>();
        _savedAnswerUseCase = services.GetService<ISavedAnswerUseCase>();
        _transcriptionUseCase = services.GetService<ITranscriptionUseCase>();
        _imageCaptionUseCase = services.GetService<IImageCaptionUseCase>();
        _documentExtractionUseCase = services.GetService<IDocumentExtractionUseCase>();
        _documentExportUseCase = services.GetService<IDocumentExportUseCase>();
        _audioRecorder = services.GetService<IAudioManager>()?.CreateRecorder();

        // No point showing controls that can't do anything — these resolve
        // to null only if a future mode forgets to register them (both
        // Standalone and McpClient mode register everything here today).
        MicButton.IsVisible = _transcriptionUseCase is not null && _audioRecorder is not null;
        AttachButton.IsVisible = _imageCaptionUseCase is not null || _documentExtractionUseCase is not null;

        if (!startupStatus.Success)
        {
            StartupErrorLabel.Text = startupStatus.ErrorMessage;
            StartupErrorLabel.IsVisible = true;
            SetInputEnabled(false);
            return;
        }

        // Defensive only at this point — IChatUseCase is registered in both
        // Standalone and McpClient mode (see MauiProgram.cs) — but kept in
        // case a future mode forgets to register it, same "fail with a
        // clear message" spirit as the startup-failure branch above.
        if (_chatUseCase is null)
        {
            ChatUnavailableLabel.Text =
                "Multi-turn chat isn't available — IChatUseCase wasn't registered for this " +
                "Deployment:Mode. This is a configuration/wiring bug, not expected in normal use.";
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

    private async void OnSessionTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid { BindingContext: ChatSessionSummary summary })
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
            RenderTranscript();
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
        var hasPendingAttachment = _pendingAttachmentContext is not null;
        if ((string.IsNullOrWhiteSpace(typedText) && !hasPendingAttachment) || _chatUseCase is null)
        {
            return;
        }

        // An attachment with no typed question still needs something to
        // embed for retrieval and to persist as the turn's question text.
        if (string.IsNullOrWhiteSpace(typedText) && hasPendingAttachment)
        {
            typedText = "What can you tell me about the attached file?";
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
        // reloaded session shows exactly what was typed. The attachment
        // content travels as ChatUseCase's separate attachmentContext
        // parameter instead of being folded into the question text:
        // ChatCompletionUseCase's system prompt only allows answering from
        // the "Context:" block, so content smuggled into the question
        // would just get refused by that same grounding rule — see
        // ChatCompletionUseCase.BuildPrompt.
        var attachmentContext = hasPendingAttachment ? _pendingAttachmentContext : null;
        var attachedFileName = _pendingAttachmentLabel;

        // Shown in the live transcript bubble only — not persisted (the
        // session stores typedText alone), so reopening this session later
        // won't show the 📎 decoration for this turn. A real per-message
        // attachment record would need ChatMessageEntry to carry one;
        // not built this round.
        var displayText = hasPendingAttachment ? $"📎 {attachedFileName}\n{typedText}" : typedText;

        QuestionEntry.Text = string.Empty;
        _pendingAttachmentLabel = null;
        _pendingAttachmentContext = null;
        PendingAttachmentRow.IsVisible = false;
        SetInputEnabled(false);

        var userItem = new TranscriptItem { Role = ChatMessageRole.User, Text = displayText };
        var assistantItem = new TranscriptItem
        {
            Role = ChatMessageRole.Assistant,
            Text = string.Empty,
            IsGenerating = true,
            SourceQuestion = typedText
        };
        _transcript.Add(userItem);
        _transcript.Add(assistantItem);
        var assistantIndex = _transcript.Count - 1; // stable for this turn — RenderTranscript() below doesn't reorder anything
        RenderTranscript();

        var answerSoFar = new StringBuilder();

        try
        {
            await foreach (var token in _chatUseCase.SendMessageAsync(sessionId, typedText, attachmentContext, attachedFileName, cts.Token))
            {
                if (!token.IsFinal)
                {
                    answerSoFar.Append(token.Text);
                    var partial = answerSoFar.ToString();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistantItem.IsGenerating = false; // first token arrived — stop showing "Thinking…"
                        assistantItem.Text = partial;
                        _ = UpdateStreamingMessageAsync(assistantIndex);
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
                        _ = UpdateStreamingMessageAsync(assistantIndex);
                    });
                }
            }

            // Offer a document export if the question sounded like a
            // generation request ("write this up as a report", etc.) — see
            // DocumentGenerationIntentDetector's doc comment. Only offered
            // when there's an actual export use case to hand the click to.
            if (_documentExportUseCase is not null)
            {
                var intent = DocumentGenerationIntentDetector.Detect(typedText);
                if (intent is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        assistantItem.ExportIntent = intent;
                        _ = UpdateStreamingMessageAsync(assistantIndex);
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                assistantItem.IsGenerating = false;
                assistantItem.Text = errorText;
                _ = UpdateStreamingMessageAsync(assistantIndex);
            });
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
        RenderTranscript();
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
        RenderTranscript();
    }

    /// <summary>
    /// Full rebuild of the transcript HTML, reloaded into
    /// <see cref="TranscriptWebView"/> — see MainPage.xaml's Grid.Row="2"
    /// doc comment for why a WebView instead of any native scrollable
    /// control. Called whenever the set of messages itself changes (a
    /// message added, a session opened/cleared) — every message needs a
    /// DOM node either way, so there's no cheaper partial update for that
    /// case. Editing an *existing* message's content (streaming tokens,
    /// citations, export offer) goes through
    /// <see cref="UpdateStreamingMessageAsync"/> instead, which patches the
    /// DOM in place via JS rather than reloading the whole page — a full
    /// reload on every streamed token would flicker and would drop
    /// whatever text the user currently has selected.
    /// </summary>
    private void RenderTranscript()
    {
        TranscriptWebView.Source = new HtmlWebViewSource { Html = BuildTranscriptHtml() };
    }

    /// <summary>
    /// Builds the whole transcript as one HTML document. Every message div
    /// always renders its .thinking/.citations/.export-link elements (just
    /// hidden via style="display:none" when not applicable) rather than
    /// omitting them conditionally, specifically so
    /// <see cref="UpdateStreamingMessageAsync"/> can always find them by a
    /// stable selector without needing to know whether this rebuild
    /// included them. Message text uses CSS white-space:pre-wrap +
    /// user-select:text — the latter is what actually gets us real
    /// click-and-drag selection, which none of the native-control attempts
    /// (Label, Editor, CollectionView row templates) could offer.
    /// </summary>
    private string BuildTranscriptHtml()
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <style>
              html, body { margin: 0; padding: 0; }
              body {
                font-family: 'Segoe UI', -apple-system, sans-serif;
                padding: 4px 8px 16px 8px;
                color: #1a1a1a;
                font-size: 15px;
              }
              .empty { color: gray; font-style: italic; padding: 12px 4px; }
              .msg { margin-bottom: 20px; }
              .role { font-weight: 600; font-size: 12px; margin-bottom: 4px; }
              .role.user { color: #444444; }
              .role.assistant { color: #512BD4; }
              .thinking { font-size: 13px; color: gray; font-style: italic; margin-bottom: 4px; }
              .text { white-space: pre-wrap; line-height: 1.45; user-select: text; -webkit-user-select: text; }
              .citations { font-size: 11px; color: gray; white-space: pre-wrap; margin-top: 6px; }
              .action-link {
                display: inline-block;
                margin-top: 8px;
                font-size: 12px;
                padding: 4px 10px;
                border: 1px solid #cccccc;
                border-radius: 4px;
                color: #512BD4;
                text-decoration: none;
                user-select: none;
              }
              .action-link:hover { background: #f2f0fa; }
            </style>
            </head>
            <body>
            """);

        if (_transcript.Count == 0)
        {
            sb.Append("""<div class="empty">Ask a question to get started.</div>""");
        }

        for (var i = 0; i < _transcript.Count; i++)
        {
            var item = _transcript[i];
            var roleClass = item.Role == ChatMessageRole.User ? "user" : "assistant";
            var thinkingDisplay = item.IsGenerating ? "block" : "none";
            var citationsDisplay = string.IsNullOrEmpty(item.CitationsText) ? "none" : "block";
            var exportDisplay = item.ShowExportButton ? "inline-block" : "none";

            sb.Append($"""<div class="msg" id="msg-{i}">""");
            sb.Append($"""<div class="role {roleClass}">{HtmlEncode(item.RoleLabel)}</div>""");
            sb.Append($"""<div class="thinking" style="display:{thinkingDisplay}">Thinking…</div>""");
            sb.Append($"""<div class="text">{HtmlEncode(item.Text)}</div>""");
            sb.Append($"""<div class="citations" style="display:{citationsDisplay}">{HtmlEncode(item.CitationsText)}</div>""");
            sb.Append($"""<a class="action-link" href="looma-export:{i}" style="display:{exportDisplay}">{HtmlEncode(item.ExportButtonText)}</a>""");
            sb.Append("</div>");
        }

        sb.Append("""
            <script>window.scrollTo(0, document.body.scrollHeight);</script>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    /// <summary>
    /// Patches message <paramref name="index"/>'s DOM node in place via
    /// EvaluateJavaScriptAsync instead of reloading the whole page — see
    /// RenderTranscript's doc comment for why. If the WebView hasn't
    /// finished its first load yet (id lookup returns nothing, or the
    /// script call itself throws) this falls back to a full
    /// <see cref="RenderTranscript"/> rather than silently doing nothing.
    /// </summary>
    private async Task UpdateStreamingMessageAsync(int index)
    {
        if (index < 0 || index >= _transcript.Count)
        {
            return;
        }

        var item = _transcript[index];
        var textJson = JsonSerializer.Serialize(item.Text);
        var citationsJson = JsonSerializer.Serialize(item.CitationsText ?? string.Empty);
        var exportLabelJson = JsonSerializer.Serialize(item.ExportButtonText);
        var thinkingDisplay = item.IsGenerating ? "block" : "none";
        var citationsDisplay = string.IsNullOrEmpty(item.CitationsText) ? "none" : "block";
        var exportDisplay = item.ShowExportButton ? "inline-block" : "none";

        var js = $$"""
            (function() {
              var el = document.getElementById('msg-{{index}}');
              if (!el) { return false; }
              var textEl = el.querySelector('.text');
              if (textEl) { textEl.textContent = {{textJson}}; }
              var thinkingEl = el.querySelector('.thinking');
              if (thinkingEl) { thinkingEl.style.display = '{{thinkingDisplay}}'; }
              var citeEl = el.querySelector('.citations');
              if (citeEl) { citeEl.textContent = {{citationsJson}}; citeEl.style.display = '{{citationsDisplay}}'; }
              var exportEl = el.querySelector('.action-link[href^="looma-export:"]');
              if (exportEl) { exportEl.style.display = '{{exportDisplay}}'; exportEl.textContent = {{exportLabelJson}}; }
              window.scrollTo(0, document.body.scrollHeight);
              return true;
            })();
            """;

        try
        {
            var result = await TranscriptWebView.EvaluateJavaScriptAsync(js);
            if (result != "true")
            {
                RenderTranscript();
            }
        }
        catch
        {
            RenderTranscript();
        }
    }

    /// <summary>
    /// Intercepts the custom looma-export: link scheme used by
    /// BuildTranscriptHtml's per-message export link — a WebView has no
    /// native Button.Clicked equivalent, so this is the standard MAUI
    /// pattern for "let HTML trigger C# code": cancel the navigation before
    /// it actually tries to load that bogus URL, and dispatch based on it
    /// instead.
    /// </summary>
    private void OnTranscriptWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        const string exportPrefix = "looma-export:";

        if (e.Url.StartsWith(exportPrefix, StringComparison.Ordinal))
        {
            e.Cancel = true;
            if (int.TryParse(e.Url.AsSpan(exportPrefix.Length), out var index))
            {
                _ = ExportMessageAsync(index);
            }
        }
    }

    private static string HtmlEncode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

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

    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        var canImage = _imageCaptionUseCase is not null;
        var canDocument = _documentExtractionUseCase is not null;
        if (!canImage && !canDocument)
        {
            return;
        }

        // Only prompt for a choice when both are actually available —
        // otherwise just go straight to whichever one is.
        if (canImage && canDocument)
        {
            var choice = await DisplayActionSheetAsync("Attach", "Cancel", null, "Photo", "Document");
            if (choice == "Photo")
            {
                await AttachImageAsync();
            }
            else if (choice == "Document")
            {
                await AttachDocumentAsync();
            }
            return;
        }

        if (canImage)
        {
            await AttachImageAsync();
        }
        else
        {
            await AttachDocumentAsync();
        }
    }

    private async Task AttachImageAsync()
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

        // Captioning is a real model call, not instant — without this,
        // Send stays clickable the whole time and a question typed during
        // that window would go out with no attachment context at all
        // (_pendingAttachmentContext isn't set until the try block below
        // finishes), silently dropping the attachment. Same "disable input
        // while something's in flight" rule OnSendClicked already applies
        // to its own streaming response.
        SetInputEnabled(false);
        PendingAttachmentLabel.Text = $"⏳ Analyzing {photo.FileName}…";
        PendingAttachmentRow.IsVisible = true;

        try
        {
            await using var stream = await photo.OpenReadAsync();
            var result = await _imageCaptionUseCase.CaptionAsync(stream);

            var caption = result.OcrText is { Length: > 0 }
                ? $"{result.Caption} (text in image: {result.OcrText})"
                : result.Caption;

            _pendingAttachmentLabel = photo.FileName;
            _pendingAttachmentContext = caption;

            PendingAttachmentLabel.Text = $"📎 {photo.FileName}: {caption}";
        }
        catch (Exception ex)
        {
            PendingAttachmentRow.IsVisible = false;
            await DisplayAlertAsync("Couldn't analyze image", ex.Message, "OK");
        }
        finally
        {
            SetInputEnabled(true);
        }
    }

    private async Task AttachDocumentAsync()
    {
        if (_documentExtractionUseCase is null)
        {
            return;
        }

        FileResult? file;
        try
        {
            var fileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                // FilePicker filters by platform-native type descriptors, not
                // plain extensions — UTType identifiers on Mac Catalyst,
                // extension strings (with the leading dot) on Windows.
                [DevicePlatform.WinUI] = DocumentTextExtractor.SupportedExtensions,
                [DevicePlatform.MacCatalyst] = ["public.plain-text", "public.text", "com.microsoft.word.doc",
                    "org.openxmlformats.wordprocessingml.document", "com.microsoft.excel.xls",
                    "org.openxmlformats.spreadsheetml.sheet", "com.adobe.pdf"]
            });
            file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Attach a document",
                FileTypes = fileType
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't open picker", ex.Message, "OK");
            return;
        }

        if (file is null)
        {
            return; // cancelled
        }

        // Same reasoning as AttachImageAsync's SetInputEnabled(false) —
        // extracting text from a large PDF/DOCX isn't instant, and Send
        // needs to stay blocked until _pendingAttachmentContext is actually
        // populated below, or the question could go out with the
        // attachment silently missing.
        SetInputEnabled(false);
        PendingAttachmentLabel.Text = $"⏳ Reading {file.FileName}…";
        PendingAttachmentRow.IsVisible = true;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var extractedText = await _documentExtractionUseCase.ExtractAsync(stream, file.FileName);

            var truncated = extractedText.Length > MaxAttachedDocumentChars;
            var context = truncated
                ? extractedText[..MaxAttachedDocumentChars] + "\n\n(truncated — document is longer than shown here)"
                : extractedText;

            _pendingAttachmentLabel = file.FileName;
            _pendingAttachmentContext = context;

            var preview = extractedText.Length > 80 ? extractedText[..80] + "…" : extractedText;
            PendingAttachmentLabel.Text = $"📎 {file.FileName}: {preview}";
        }
        catch (NotSupportedException ex)
        {
            PendingAttachmentRow.IsVisible = false;
            await DisplayAlertAsync("Unsupported file", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            PendingAttachmentRow.IsVisible = false;
            await DisplayAlertAsync("Couldn't read document", ex.Message, "OK");
        }
        finally
        {
            SetInputEnabled(true);
        }
    }

    private void OnRemovePendingAttachmentClicked(object? sender, EventArgs e)
    {
        _pendingAttachmentLabel = null;
        _pendingAttachmentContext = null;
        PendingAttachmentRow.IsVisible = false;
    }

    /// <summary>
    /// Documents\Looma Exports — a fixed, predictable, plain-.NET
    /// location (Environment.SpecialFolder.MyDocuments works the same way
    /// on Windows and Mac Catalyst) rather than an interactive "Save As"
    /// dialog. That was tried via CommunityToolkit.Maui's IFileSaver first
    /// and reverted after a real reproduced crash — a native fault inside
    /// Microsoft.UI.Xaml.dll from a WinAppSDK version mismatch between the
    /// package and this repo's installed MAUI workload (see
    /// Directory.Packages.props). No dialog means no third-party package
    /// and nothing that can version-skew like that again.
    /// </summary>
    private static string ExportFolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Looma Exports");

    /// <summary>Reached via the looma-export: link scheme — see OnTranscriptWebViewNavigating.</summary>
    private async Task ExportMessageAsync(int index)
    {
        if (_documentExportUseCase is null || index < 0 || index >= _transcript.Count)
        {
            return;
        }

        var item = _transcript[index];
        if (item.ExportIntent is not { } intent)
        {
            return;
        }

        try
        {
            var title = item.SourceQuestion is { Length: > 0 } q ? q : "Looma answer";
            var bytes = await _documentExportUseCase.ExportAsync(title, item.Text, intent.Format);

            Directory.CreateDirectory(ExportFolderPath);

            // Timestamp suffix avoids silently overwriting a previous
            // export with the same question/title — there's no "Save As"
            // dialog anymore to catch a collision interactively.
            var fileName = $"{SanitizeFileName(title)}_{DateTimeOffset.Now:yyyyMMdd-HHmmss}{intent.Format.FileExtension()}";
            var fullPath = Path.Combine(ExportFolderPath, fileName);

            await File.WriteAllBytesAsync(fullPath, bytes);

            var copyPath = await DisplayAlertAsync("Saved", $"Saved to:\n{fullPath}", "Copy path", "OK");
            if (copyPath)
            {
                await Clipboard.Default.SetTextAsync(fullPath);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't export document", ex.Message, "OK");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60];
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "looma-answer" : cleaned;
    }

    private void SetInputEnabled(bool enabled)
    {
        QuestionEntry.IsEnabled = enabled;
        SendButton.IsEnabled = enabled;
        SaveButton.IsEnabled = enabled;
        MicButton.IsEnabled = enabled;
        AttachButton.IsEnabled = enabled;
    }

    // Rebuilds the same "📎 filename\nquestion" decoration OnSendClicked's
    // displayText uses for the live bubble — that text is deliberately
    // never persisted as-is (see ChatMessageEntry.AttachmentLabel's doc
    // comment), so a reopened session reconstructs it from the label
    // instead of just losing the 📎 decoration.
    private static TranscriptItem ToTranscriptItem(ChatMessageEntry entry) => new()
    {
        Role = entry.Role,
        Text = entry.AttachmentLabel is { Length: > 0 } label ? $"📎 {label}\n{entry.Text}" : entry.Text,
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
