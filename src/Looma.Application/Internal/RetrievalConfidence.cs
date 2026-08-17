namespace Looma.Application.Internal;

/// <summary>
/// How much a set of retrieved citations, BEFORE generation even starts,
/// suggests <c>AnswerUseCase</c>'s/<c>ChatCompletionUseCase</c>'s
/// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>
/// flow should do next — see each class's own doc comment for exactly how
/// each level changes the flow.
///
/// A coarse, citation-COUNT-based proxy, deliberately: see
/// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>'s
/// doc comment for why a quality/count-based PRE-generation check already
/// failed once as a way to decide whether the fallback should trigger —
/// adaptive thresholding (and hybrid search's own relevance filtering)
/// routinely leaves a handful of citations around even for genuinely
/// unanswerable questions, so neither a score nor a count can reliably tell
/// "will answer" from "won't" ahead of time. This enum is NOT a second
/// attempt at that same decision — <c>RefusalPrefixWatcher</c>, applied to
/// what the model actually generates, remains the only thing that decides
/// whether a web-search retry happens. What citation count CAN usefully
/// inform, without risking a wrong skip of that real check, is
/// presentation and scheduling: whether it's worth even attempting local
/// generation at all (truly zero citations), and whether it's worth
/// speculatively starting the web search early, in parallel with local
/// generation, rather than only after a confirmed refusal (a thin pool).
/// </summary>
internal enum RetrievalConfidence
{
    /// <summary>
    /// No citations survived retrieval/filtering at all — local generation
    /// has nothing to work with and, in practice, would just reproduce the
    /// refusal sentence anyway. Skip it entirely and go straight to a web
    /// search instead of paying for a near-certainly-futile local pass.
    /// </summary>
    Empty,

    /// <summary>
    /// A thin candidate pool (see <see cref="RetrievalConfidenceClassifier"/>
    /// for the exact cutoff) — local generation might still succeed, but
    /// it's cheap and worthwhile to speculatively start a web search in
    /// parallel in case it doesn't, rather than paying for that search
    /// sequentially only after the refusal is confirmed.
    /// </summary>
    Low,

    /// <summary>
    /// A normal-looking candidate pool — proceed with local generation
    /// only. <c>RefusalPrefixWatcher</c> remains the safety net if the
    /// model refuses anyway; this level just doesn't pay the extra cost of
    /// starting a web search speculatively when it's less likely to be needed.
    /// </summary>
    Confident
}

/// <summary>Classifies raw citation counts into a <see cref="RetrievalConfidence"/> level — see that enum's doc comment.</summary>
internal static class RetrievalConfidenceClassifier
{
    /// <summary>
    /// Citation count at or below this (but above zero) is <see cref="RetrievalConfidence.Low"/>.
    /// A reasoned starting point, not a measured-optimal one — same caveat
    /// as this assembly's other tunables. Deliberately NOT exposed as a
    /// <see cref="Looma.Application.Configuration.RagOptions"/> knob: it's a
    /// coarse presentation/scheduling hint (see
    /// <see cref="RetrievalConfidence"/>'s doc comment), not a
    /// precision-tunable relevance parameter like <c>MinRelevanceScore</c>.
    /// </summary>
    private const int LowConfidenceCitationCountThreshold = 1;

    public static RetrievalConfidence Classify(int citationCount) => citationCount switch
    {
        0 => RetrievalConfidence.Empty,
        _ when citationCount <= LowConfidenceCitationCountThreshold => RetrievalConfidence.Low,
        _ => RetrievalConfidence.Confident
    };
}
