using Looma.Core.Entities;
using Looma.Infrastructure.LocalStore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Looma.Infrastructure.LocalStore.Tests;

public sealed class FileSavedAnswerStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSavedAnswerStore _store;

    public FileSavedAnswerStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "looma-saved-answer-tests-" + Guid.NewGuid());
        var options = Options.Create(new ChatHistoryOptions
        {
            SavedAnswersFilePath = Path.Combine(_tempDir, "saved-answers.json")
        });
        _store = new FileSavedAnswerStore(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static SavedAnswer MakeAnswer(string title = "Test answer") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Question = "What's the policy?",
        AnswerText = "The policy is X.",
        Citations = [],
        SavedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ReturnsSameAnswer()
    {
        var answer = MakeAnswer();

        await _store.SaveAsync(answer);
        var loaded = await _store.GetAsync(answer.Id);

        Assert.NotNull(loaded);
        Assert.Equal(answer.Title, loaded!.Title);
        Assert.Equal(answer.AnswerText, loaded.AnswerText);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _store.GetAsync("does-not-exist"));
    }

    [Fact]
    public async Task ListAsync_OrdersBySavedAtDescending()
    {
        var first = MakeAnswer("First");
        await _store.SaveAsync(first);
        await Task.Delay(10);
        var second = MakeAnswer("Second") with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _store.SaveAsync(second);

        var list = await _store.ListAsync();

        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
    }

    [Fact]
    public async Task DeleteAsync_RemovesIt()
    {
        var answer = MakeAnswer();
        await _store.SaveAsync(answer);

        await _store.DeleteAsync(answer.Id);

        Assert.Null(await _store.GetAsync(answer.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotThrow()
    {
        await _store.DeleteAsync("does-not-exist");
    }
}
