using Looma.Application.Internal;
using Looma.Core.Entities;
using Xunit;

namespace Looma.Application.Tests;

public sealed class ConversationHistoryBudgetTests
{
    private static ChatMessageEntry Entry(ChatMessageRole role, string text) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Role = role,
        Text = text,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public void Trim_ContextSizeNull_ReturnsHistoryUnchanged()
    {
        var history = new List<ChatMessageEntry>
        {
            Entry(ChatMessageRole.User, new string('a', 10_000))
        };

        var result = ConversationHistoryBudget.Trim(history, modelContextSizeTokens: null, reservedChars: 0);

        Assert.Same(history[0], Assert.Single(result));
    }

    [Fact]
    public void Trim_EmptyHistory_ReturnsEmpty()
    {
        var result = ConversationHistoryBudget.Trim([], modelContextSizeTokens: 8192, reservedChars: 0);

        Assert.Empty(result);
    }

    [Fact]
    public void Trim_EverythingFitsWithinBudget_ReturnsAllEntriesInOriginalOrder()
    {
        var history = new List<ChatMessageEntry>
        {
            Entry(ChatMessageRole.User, "short question one"),
            Entry(ChatMessageRole.Assistant, "short answer one"),
            Entry(ChatMessageRole.User, "short question two"),
        };

        var result = ConversationHistoryBudget.Trim(history, modelContextSizeTokens: 8192, reservedChars: 0);

        Assert.Equal(history, result);
    }

    /// <summary>
    /// Regression test for the real bug this class fixes: a long session's
    /// accumulated history exceeding the model's real context window broke
    /// every subsequent turn. A small enough context size must drop the
    /// oldest turns rather than sending everything regardless of size.
    /// </summary>
    [Fact]
    public void Trim_HistoryExceedsBudget_DropsOldestTurnsFirst()
    {
        var history = new List<ChatMessageEntry>
        {
            Entry(ChatMessageRole.User, new string('a', 2000)),
            Entry(ChatMessageRole.Assistant, new string('b', 2000)),
            Entry(ChatMessageRole.User, new string('c', 2000)),
            Entry(ChatMessageRole.Assistant, new string('d', 2000)),
        };

        // 8192 tokens * 3 chars/token = ~24,576 char budget; reserving all
        // but ~3000 of it leaves room for only the newest ~1-2 turns.
        var result = ConversationHistoryBudget.Trim(history, modelContextSizeTokens: 8192, reservedChars: 21_500);

        Assert.DoesNotContain(history[0], result);
        Assert.Contains(history[^1], result);
        // Kept entries must stay in original (oldest-first) order.
        Assert.Equal(result, result.OrderBy(history.IndexOf));
    }

    [Fact]
    public void Trim_ReservedCharsAloneExceedsBudget_StillKeepsMostRecentEntry()
    {
        var history = new List<ChatMessageEntry>
        {
            Entry(ChatMessageRole.User, "older turn"),
            Entry(ChatMessageRole.Assistant, "most recent turn"),
        };

        // reservedChars alone already exceeds the whole token budget.
        var result = ConversationHistoryBudget.Trim(history, modelContextSizeTokens: 100, reservedChars: 1_000_000);

        var kept = Assert.Single(result);
        Assert.Equal(history[^1], kept);
    }
}
