using Looma.Application.Internal;
using Xunit;

namespace Looma.Application.Tests;

public sealed class RetrievalConfidenceClassifierTests
{
    [Fact]
    public void Classify_ZeroCitations_ReturnsEmpty()
    {
        Assert.Equal(RetrievalConfidence.Empty, RetrievalConfidenceClassifier.Classify(0));
    }

    [Fact]
    public void Classify_OneCitation_ReturnsLow()
    {
        Assert.Equal(RetrievalConfidence.Low, RetrievalConfidenceClassifier.Classify(1));
    }

    [Fact]
    public void Classify_TwoOrMoreCitations_ReturnsConfident()
    {
        Assert.Equal(RetrievalConfidence.Confident, RetrievalConfidenceClassifier.Classify(2));
        Assert.Equal(RetrievalConfidence.Confident, RetrievalConfidenceClassifier.Classify(5));
    }
}
