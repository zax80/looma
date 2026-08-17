using Looma.Application;
using Looma.Application.Internal;
using Xunit;

namespace Looma.Application.Tests;

public sealed class RefusalPrefixWatcherTests
{
    [Fact]
    public void Observe_UnrelatedTextFromTheFirstChunk_PassesThroughImmediately()
    {
        var watcher = new RefusalPrefixWatcher();

        var released = watcher.Observe("Looma is a local-first document intelligence platform.");

        Assert.Equal("Looma is a local-first document intelligence platform.", released);
        Assert.False(watcher.IsConfirmedRefusal);
    }

    [Fact]
    public void Observe_RealAnswerStreamedInSmallChunks_EventuallyReleasesEverythingUnaltered()
    {
        var watcher = new RefusalPrefixWatcher();
        var chunks = new[] { "Looma ", "is ", "a ", "local-first ", "platform." };

        var released = string.Concat(chunks.Select(watcher.Observe));

        Assert.Equal("Looma is a local-first platform.", released);
        Assert.False(watcher.IsConfirmedRefusal);
    }

    [Fact]
    public void Observe_ExactRefusalSentenceInOneChunk_BuffersEverything_ConfirmedAtStreamEnd()
    {
        var watcher = new RefusalPrefixWatcher();

        var released = watcher.Observe(GroundedAnswer.NoAnswerSentence);

        Assert.Equal(string.Empty, released);
        Assert.True(watcher.IsConfirmedRefusal);
        Assert.Equal(string.Empty, watcher.FlushRemaining());
    }

    [Fact]
    public void Observe_ExactRefusalSentenceStreamedTokenByToken_StillConfirmedAtStreamEnd()
    {
        var watcher = new RefusalPrefixWatcher();
        var releasedTotal = string.Empty;
        var words = GroundedAnswer.NoAnswerSentence.Split(' ');

        // Reconstructs the sentence EXACTLY (no extra trailing space after
        // the last word) — an added trailing space would make the
        // accumulated text longer than the real sentence and legitimately
        // diverge, which is a different scenario than this test is for.
        for (var i = 0; i < words.Length; i++)
        {
            var chunk = i < words.Length - 1 ? words[i] + " " : words[i];
            releasedTotal += watcher.Observe(chunk);
        }

        Assert.Equal(string.Empty, releasedTotal);
        Assert.True(watcher.IsConfirmedRefusal);
    }

    [Fact]
    public void Observe_RefusalPrefixThatThenDiverges_FlushesEverythingBuffered_NotConfirmedRefusal()
    {
        var watcher = new RefusalPrefixWatcher();

        var releasedBeforeDivergence = watcher.Observe("The provided context does not contain this information");
        Assert.Equal(string.Empty, releasedBeforeDivergence);

        // Diverges here — real refusal sentence ends with a period and
        // nothing else; this keeps going, so it can never match exactly.
        var releasedAtDivergence = watcher.Observe(", but here is what I can tell you anyway:");

        Assert.Equal(
            "The provided context does not contain this information, but here is what I can tell you anyway:",
            releasedAtDivergence);
        Assert.False(watcher.IsConfirmedRefusal);

        // Everything after divergence passes straight through, no more buffering.
        var releasedAfter = watcher.Observe(" more detail.");
        Assert.Equal(" more detail.", releasedAfter);
    }

    [Fact]
    public void Observe_GenuinelyShortAnswerThatIsAStrictPrefixOfTheRefusalSentence_IsNotConfirmedRefusal()
    {
        var watcher = new RefusalPrefixWatcher();

        var released = watcher.Observe("The provided context");

        Assert.Equal(string.Empty, released);
        Assert.False(watcher.IsConfirmedRefusal);
        Assert.Equal("The provided context", watcher.FlushRemaining());
    }

    [Fact]
    public void Observe_LeadingWhitespaceBeforeRefusalSentence_StillConfirmedAtStreamEnd()
    {
        var watcher = new RefusalPrefixWatcher();

        var released = watcher.Observe("  " + GroundedAnswer.NoAnswerSentence);

        Assert.Equal(string.Empty, released);
        Assert.True(watcher.IsConfirmedRefusal);
    }

    [Fact]
    public void FlushRemaining_AfterDivergenceAlreadyReleased_ReturnsEmpty()
    {
        var watcher = new RefusalPrefixWatcher();
        watcher.Observe("Definitely not a refusal.");

        Assert.Equal(string.Empty, watcher.FlushRemaining());
    }
}
