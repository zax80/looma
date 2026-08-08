using System.Collections.ObjectModel;
using System.Text;
using Looma.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.UI.MAUI.Desktop;

/// <summary>
/// Chat-style answer view — the one page this milestone builds, per the
/// project's own "Desktop first / chat-style answer view only" scoping
/// decision. Mirrors Looma.CLI's AnswerCommand: streams
/// <see cref="Looma.Core.Entities.AnswerToken"/> as generated, citations
/// arrive on the final token. No indexing UI here — indexing stays a
/// `looma index` CLI job for now.
///
/// Constructor-injected via the DI container (registered
/// AddTransient&lt;MainPage&gt; in MauiProgram.cs) — Shell's
/// ContentTemplate="{DataTemplate local:MainPage}" resolves it through
/// the app's IServiceProvider, same pattern documented for .NET MAUI
/// Shell + DI. <see cref="IAnswerUseCase"/> is resolved from
/// <paramref name="services"/> rather than injected directly because
/// it's only registered when MauiProgram's composition root actually
/// succeeded (see <see cref="StartupStatus"/>) — the page needs to run
/// (and show an error) even when it isn't.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly IAnswerUseCase? _answerUseCase;
    private readonly ObservableCollection<string> _citations = new();
    private CancellationTokenSource? _pendingQuestionCts;

    public MainPage(IServiceProvider services, StartupStatus startupStatus)
    {
        InitializeComponent();

        CitationsView.ItemsSource = _citations;
        _answerUseCase = services.GetService<IAnswerUseCase>();

        if (!startupStatus.Success)
        {
            StartupErrorLabel.Text = startupStatus.ErrorMessage;
            StartupErrorLabel.IsVisible = true;
            QuestionEntry.IsEnabled = false;
            AskButton.IsEnabled = false;
        }
    }

    private async void OnAskClicked(object? sender, EventArgs e)
    {
        var question = QuestionEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(question) || _answerUseCase is null)
        {
            return;
        }

        // A new question supersedes whatever the previous one was still
        // streaming — cancel it rather than let two answers interleave.
        _pendingQuestionCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pendingQuestionCts = cts;

        QuestionEntry.IsEnabled = false;
        AskButton.IsEnabled = false;
        AnswerLabel.Text = string.Empty;
        _citations.Clear();

        var answerSoFar = new StringBuilder();

        try
        {
            await foreach (var token in _answerUseCase.AnswerAsync(question, cts.Token))
            {
                if (!token.IsFinal)
                {
                    answerSoFar.Append(token.Text);
                    var partialText = answerSoFar.ToString();
                    MainThread.BeginInvokeOnMainThread(() => AnswerLabel.Text = partialText);
                    continue;
                }

                if (token.Citations is { Count: > 0 } citations)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        for (var i = 0; i < citations.Count; i++)
                        {
                            var citation = citations[i];
                            _citations.Add(
                                $"[{i + 1}] {citation.SourceId} (lines {citation.Metadata.StartLine}-{citation.Metadata.EndLine})");
                        }
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer question — nothing to show.
        }
        catch (Exception ex)
        {
            var message = $"Error: {ex.Message}";
            MainThread.BeginInvokeOnMainThread(() => AnswerLabel.Text = message);
        }
        finally
        {
            if (_pendingQuestionCts == cts)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    QuestionEntry.IsEnabled = true;
                    AskButton.IsEnabled = true;
                });
            }
        }
    }
}
