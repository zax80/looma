using System.Runtime.CompilerServices;
using System.Text;
using Looma.Application.Configuration;
using Looma.Application.DocumentGeneration;
using Looma.Application.Internal;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Looma.Application.UseCases;

/// <summary>
/// Retrieval + grounded generation for one chat turn — the same
/// system-prompt/context-block design as <c>AnswerUseCase</c>, extended
/// with conversation history and an optional attachment context block.
///
/// Attachment handling: <paramref name="attachmentContext"/>-equivalent
/// material is added to the SAME context block the system prompt grants
/// permission to answer from — not folded into the question text. That was
/// tried first (in the pre-split <c>ChatUseCase</c>) and was a real bug:
/// the grounding rule only permits answering from "the context below," so
/// a caption smuggled into the question got refused by that same rule
/// even though the information was technically present in the prompt.
///
/// Adaptive relevance thresholding: retrieval's citation cutoff used to be
/// a single flat <see cref="RagOptions.MinRelevanceScore"/> applied the
/// same way to every query — see
/// <see cref="RagOptions.EnableAdaptiveThreshold"/>'s doc comment for why
/// that under-serves both easy and hard queries. <c>RagRetrieval</c> now
/// keeps whichever candidates score close to THIS query's own best match,
/// not just "above a fixed line" — no extra retrieval or LLM call, just a
/// smarter filter over the same TopK candidates.
///
/// Query reformulation: retrieval used to be keyed on the latest message
/// alone, not the full conversation — a follow-up like "what about the
/// other one?" retrieved poorly since the retrieval query itself had no
/// context beyond that one message (this is the general form of the
/// "who's the author?" bug described below, for ordinary document
/// retrieval rather than attachments specifically).
/// <see cref="ReformulateQueryAsync"/> now condenses history + follow-up
/// into a self-contained search query before embedding — toggleable via
/// <see cref="RagOptions.EnableQueryReformulation"/> since it's a real
/// latency-for-quality trade-off (one more non-streaming LLM call before
/// the visible answer starts streaming), not a strict improvement in
/// every dimension. The rewritten query is used ONLY for retrieval — the
/// model still answers the user's ORIGINAL wording in
/// <see cref="CompleteAsync"/>'s <c>message</c> parameter, never the
/// rewritten version, so responses stay natural rather than echoing a
/// robotic search-query phrasing.
///
/// Grounding scope: the system prompt originally forbade using prior
/// conversation turns as a source of facts at all (only for understanding
/// pronouns/follow-ups) — real-world testing surfaced a bad case for that:
/// "can you say that in English?" right after Looma had just stated a
/// quote got refused, because the quote wasn't repeated in THIS turn's
/// context block, even though Looma had just said it. The rule now also
/// permits translating/rephrasing/summarizing something Looma already
/// said earlier in the same conversation — still never permits stating a
/// NEW fact that wasn't grounded somewhere (this turn's context, or an
/// earlier turn's own grounded answer).
///
/// Sticky attachment memory: reusing Looma's own prior answers (above)
/// only helps if Looma happened to restate the fact being asked about — a
/// second real case hit in testing: attaching an image, asking about it,
/// then asking "who's the author?" as a THIRD turn got a confidently
/// WRONG answer (retrieval pulled in an unrelated indexed image that
/// merely scored well on "quote"/"author" similarity, and the grounding
/// rule let the model answer from it since it was technically "in the
/// context"). The actual author was in the image's caption, never
/// restated by Looma, and never available again once that turn ended.
/// <see cref="BuildStickyAttachmentsBlock"/> fixes this by re-surfacing
/// every attachment's actual content from earlier in the SAME session
/// (budget-capped, most recent first) into the context block on every
/// later turn — not just what Looma chose to say about it.
///
/// Document-export follow-ups: a real case hit in testing — Looma
/// correctly answered "who told [quote]?", then "Can you write it in a
/// pdf?" got refused with the no-answer sentence, even though the quote
/// had just been stated. Root cause: <c>MainPage</c>'s client-side
/// <see cref="DocumentGenerationIntentDetector"/> (which decides whether
/// to offer an "Export as..." button) had zero influence on THIS class's
/// prompt — the model saw only the raw text and, per the grounding rule
/// above, treated "write it in a pdf" as an ordinary question it
/// couldn't answer rather than a request to restate already-grounded
/// material. <see cref="BuildPrompt"/> now runs the same detector over
/// the current message and, when it matches, swaps in a dedicated
/// export-focused instruction sentence instead of the ordinary
/// answer-or-refuse one.
///
/// That swap (a whole separate instruction, not an appended note) is
/// itself the fix for a second real case: appending "...but don't refuse"
/// right after "...if not covered, refuse", in the same sentence, still
/// wasn't enough — "Can you create a pdf document, about the coffee?"
/// got refused even with a genuinely relevant coffee-brewing.txt chunk
/// retrieved into the context, apparently because the model checked the
/// literal question ("can you create a document") against the context
/// rather than the underlying topic. Giving the model exactly one
/// instruction for this case, not two competing ones to reconcile itself,
/// is what actually fixed it.
///
/// Vague follow-ups after a failed prior turn: a third real case — a chat
/// question failed (Qdrant briefly unreachable) before Looma ever replied.
/// The next message, "And now?", retrieved the right chunk fine (query
/// reformulation correctly resolved it against the dangling unanswered
/// question) but generation still refused. Two instruction-wording fixes
/// were tried here in <see cref="BuildPrompt"/> — a parenthetical hint,
/// then a dedicated directive stating the resolved question outright —
/// and BOTH failed identically, confirmed via the actual saved session.
/// The real root cause turned out to be structural, not wording: the
/// failed prior turn left a User message with no matching Assistant reply
/// (see <c>ChatUseCase.SendMessageAsync</c>'s doc comment), so the model
/// was fed two consecutive User messages with no Assistant turn between
/// them — a conversation shape no chat model is trained to expect, which
/// no amount of instruction text in the CURRENT turn could reliably talk
/// it out of. The actual fix lives in <c>ChatUseCase</c> and
/// <c>RemoteChatUseCase</c> (always append a synthetic Assistant entry
/// when a turn fails, keeping history alternating). The dedicated
/// directive below is kept regardless — it's a genuine improvement for
/// ordinary follow-ups whenever reformulation meaningfully changes the
/// query, independent of this bug.
/// </summary>
public sealed class ChatCompletionUseCase : IChatCompletionUseCase
{
    private const string SystemPrompt =
        "You are Looma, a local document assistant having a multi-turn conversation. Answer using " +
        "the information in the provided context — this may include excerpts retrieved from indexed " +
        "documents, and/or content from a file (an image's description, or a document's extracted " +
        "text) the user attached to this or an earlier message in this conversation; all of these " +
        "are equally valid material to answer from. Summarize, combine, or explain what's there freely, " +
        "even for a broad or open-ended question, as long as the context actually contains material " +
        "relevant to it. You may also translate, rephrase, summarize, reformat, or otherwise " +
        "re-present something you already said earlier in this same conversation, even if it isn't " +
        "repeated in the context below — reusing your own prior grounded answer that way is fine, " +
        "since it was already grounded when you said it the first time. What you must never do is " +
        "state a fact, name, number, or claim that wasn't already present either in the context " +
        "below or in one of your own earlier answers in this conversation, no matter how confident " +
        "you are it's correct. If neither the context below nor anything you've already said covers " +
        "the current question, respond with exactly this sentence and nothing else: \"The provided " +
        "context does not contain this information.\" Use the prior conversation turns to understand " +
        "what the user is asking about (pronouns, follow-ups) and, as described above, as material " +
        "you may reformulate — but never as license to add anything beyond what was already " +
        "grounded.";

    /// <summary>
    /// Total character budget across ALL sticky attachments combined in
    /// one prompt — a session with several attached files over time could
    /// otherwise grow the context block without bound. A blunt cap, same
    /// spirit as MainPage's MaxAttachedDocumentChars on the MAUI side
    /// (which already truncates a single attachment before it's even sent
    /// here) — not token-budget-aware, just a simple safeguard.
    /// </summary>
    private const int MaxStickyAttachmentChars = 4000;

    /// <summary>
    /// How many of the most recent prior turns get fed into query
    /// reformulation (see <see cref="ReformulateQueryAsync"/>) — recent
    /// turns are what usually determine what a follow-up's pronouns refer
    /// to; older history rarely changes that, and keeping this small keeps
    /// the reformulation prompt (and therefore its latency, which is
    /// already this feature's main cost) small too.
    /// </summary>
    private const int MaxReformulationHistoryTurns = 6;

    private const string ReformulationSystemPrompt =
        "You rewrite a follow-up question into a standalone search query, using the conversation " +
        "so far to resolve pronouns and implicit references (\"it\", \"that\", \"the other one\", " +
        "\"this image\", etc.) into their actual subject. Output ONLY the rewritten query and " +
        "nothing else — no explanation, no quotation marks, no preamble. If the follow-up is " +
        "already standalone and doesn't depend on anything earlier in the conversation, output it " +
        "completely unchanged. Keep it a short, specific search query, not a full sentence.";

    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IChatClient _chatClient;
    private readonly RagOptions _ragOptions;

    public ChatCompletionUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IChatClient chatClient,
        IOptions<RagOptions> ragOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _chatClient = chatClient;
        _ragOptions = ragOptions.Value;
    }

    public async IAsyncEnumerable<AnswerToken> CompleteAsync(
        IReadOnlyList<ChatMessageEntry> history,
        string message,
        string? attachmentContext = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var retrievalQuery = _ragOptions.EnableQueryReformulation
            ? await ReformulateQueryAsync(history, message, cancellationToken).ConfigureAwait(false)
            : message;

        var queryEmbedding = await _embeddingGenerator
            .GenerateVectorAsync(retrievalQuery, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var citations = await RagRetrieval
            .RetrieveCitationsAsync(_vectorStore, queryEmbedding, _ragOptions, cancellationToken)
            .ConfigureAwait(false);

        var promptMessages = BuildPrompt(history, message, retrievalQuery, citations, attachmentContext);

        var chatOptions = new ChatOptions { Temperature = _ragOptions.AnswerTemperature };
        if (_ragOptions.MaxAnswerTokens is { } maxTokens)
        {
            chatOptions.MaxOutputTokens = maxTokens;
        }

        await foreach (var update in _chatClient.GetStreamingResponseAsync(promptMessages, chatOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new AnswerToken { Text = update.Text, IsFinal = false };
            }
        }

        yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = citations };
    }

    /// <summary>
    /// Rewrites <paramref name="currentMessage"/> into a standalone search
    /// query using recent conversation history, so retrieval isn't blind
    /// to what the conversation is actually about — see the class doc
    /// comment's "Query reformulation" section. Returns
    /// <paramref name="currentMessage"/> unchanged on the very first turn
    /// (no history to resolve pronouns against) or if the reformulation
    /// call itself fails for any reason: this is a retrieval-quality
    /// optimization, not a correctness requirement, so a model hiccup here
    /// should degrade to "search with the raw follow-up, same as before
    /// this feature existed" rather than fail the whole turn.
    /// </summary>
    private async Task<string> ReformulateQueryAsync(
        IReadOnlyList<ChatMessageEntry> priorMessages,
        string currentMessage,
        CancellationToken cancellationToken)
    {
        if (priorMessages.Count == 0)
        {
            return currentMessage;
        }

        var recentHistory = priorMessages.Count > MaxReformulationHistoryTurns
            ? priorMessages.Skip(priorMessages.Count - MaxReformulationHistoryTurns).ToList()
            : priorMessages;

        var historyText = new StringBuilder();
        foreach (var entry in recentHistory)
        {
            historyText.Append(entry.Role == ChatMessageRole.User ? "User: " : "Assistant: ")
                .Append(entry.Text).Append('\n');
        }

        var reformulationMessages = new List<ChatMessage>
        {
            new(ChatRole.System, ReformulationSystemPrompt),
            new(ChatRole.User,
                $"Conversation so far:\n{historyText}\n" +
                $"Follow-up question: {currentMessage}\n\n" +
                "Standalone search query:")
        };

        try
        {
            // Non-streaming — there's no point streaming a query rewrite
            // token by token when nothing downstream can use it until it's
            // complete anyway. Temperature 0 and a small output cap: this
            // is a mechanical rewriting task, not creative generation, and
            // it's already adding latency before the real (streaming)
            // answer can start — no reason to let it run long.
            var response = await _chatClient.GetResponseAsync(
                reformulationMessages,
                new ChatOptions { Temperature = 0f, MaxOutputTokens = 60 },
                cancellationToken).ConfigureAwait(false);

            var rewritten = response.Text?.Trim();
            return string.IsNullOrWhiteSpace(rewritten) ? currentMessage : rewritten;
        }
        catch (OperationCanceledException)
        {
            // A genuinely cancelled turn (superseded by a newer message,
            // same as MainPage.OnSendClicked's own handling) should still
            // cancel — only actual failures fall back to the raw message.
            throw;
        }
        catch (Exception)
        {
            return currentMessage;
        }
    }

    private static List<ChatMessage> BuildPrompt(
        IReadOnlyList<ChatMessageEntry> priorMessages,
        string currentMessage,
        string retrievalQuery,
        IReadOnlyList<DocumentChunk> citations,
        string? attachmentContext)
    {
        var context = new StringBuilder();

        // Attached content first (an image's caption, or a document's
        // extracted text — the caller decides which and formats
        // attachmentContext accordingly, this method doesn't need to
        // know), clearly labeled, ahead of the retrieved document
        // excerpts — see the class doc comment for why this lives here
        // instead of the question text.
        if (!string.IsNullOrWhiteSpace(attachmentContext))
        {
            context.Append("Attached content: ").Append(attachmentContext).Append("\n\n");
        }

        // See the class doc comment's "Sticky attachment memory" section —
        // this is what lets a THIRD turn ("who's the author?") draw on
        // what a file actually said, not just whatever Looma happened to
        // restate about it on the turn right after it was attached.
        context.Append(BuildStickyAttachmentsBlock(priorMessages));

        if (citations.Count == 0)
        {
            if (context.Length == 0)
            {
                context.Append("(no matching context was found in the index)");
            }
        }
        else
        {
            for (var i = 0; i < citations.Count; i++)
            {
                context.Append('[').Append(i + 1).Append("] (").Append(citations[i].SourceId).Append(")\n")
                    .Append(citations[i].Content).Append("\n\n");
            }
        }

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };

        // Genuine prior turns, verbatim — gives the model conversational
        // context (pronouns, follow-ups) without re-sending past citations
        // (Microsoft.Extensions.AI's ChatMessage has nowhere clean to carry
        // them, and the model doesn't need earlier citations to answer the
        // current question — only the current context block below).
        foreach (var entry in priorMessages)
        {
            messages.Add(new ChatMessage(
                entry.Role == ChatMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                entry.Text));
        }

        // See the class doc comment's "Document-export follow-ups" section
        // — same detector MainPage uses to decide whether to offer an
        // "Export as..." button, reused here so the model itself knows a
        // message like "write it in a pdf" is asking it to restate
        // already-grounded material for export, not asking a new question
        // it can't answer. This is a SEPARATE instruction sentence, not an
        // appended note on the ordinary one (an earlier version appended a
        // "don't refuse" note right after "if not covered, refuse" in the
        // very same sentence — a real, reproduced failure: a small local
        // model given both directives in one breath still refused a
        // request like "Can you create a pdf document, about the coffee?"
        // even with a genuinely relevant coffee-brewing.txt chunk sitting
        // right there in the context, apparently reading the literal
        // question "can you create a document" — not the underlying topic
        // — as the thing to check against the context. One unambiguous
        // instruction per case removes that conflict instead of trying to
        // out-word it.
        var isExportRequest = DocumentGenerationIntentDetector.Detect(currentMessage) is not null;

        // A real, CONFIRMED case (verified against the actual saved chat
        // session, not just inferred): a vague follow-up ("And now?") sent
        // right after an earlier turn that failed before Looma ever
        // replied (e.g. Qdrant was briefly down) — retrieval found the
        // right chunk (ReformulateQueryAsync correctly resolved "And now?"
        // into "What is the current status of the coffee?"), but
        // generation still refused. The first attempt at fixing this
        // appended the resolved query as a quiet parenthetical next to the
        // literal question — confirmed NOT enough; the model just ignored
        // it and refused anyway. Same lesson as the export-request case
        // right above: a small local model needs ONE clear instruction to
        // act on, not a footnote competing with the literal wording for
        // attention. This branch swaps in a dedicated instruction that
        // explicitly states the resolved question as what to actually
        // answer, the same structural fix that worked for exports.
        var reformulatedDiffersFromMessage =
            !string.Equals(retrievalQuery.Trim(), currentMessage.Trim(), StringComparison.OrdinalIgnoreCase);

        var instructionText = isExportRequest
            ? "This message is a request to prepare the relevant information from the context " +
              "above (and/or anything you already said earlier in this conversation) for export " +
              "as a document. Write that information out clearly and completely, the same as you " +
              "would to answer a plain question about the same underlying topic — don't refuse " +
              "just because the message is phrased as a request to create/export/write up a " +
              "document rather than as a question, and don't comment on the export/file/PDF " +
              "mechanics themselves (that happens separately, client-side, after you answer). " +
              $"Only reply with exactly \"{GroundedAnswer.NoAnswerSentence}\" if the context above " +
              "and everything you've already said truly contain nothing relevant to the " +
              "underlying topic at all."
            : reformulatedDiffersFromMessage
            ? $"The user's message, \"{currentMessage}\", is a follow-up whose real meaning depends on " +
              $"this conversation. Based on the conversation so far, what it's actually asking is: " +
              $"\"{retrievalQuery}\" — treat that restated question as the one to answer, not the " +
              "follow-up's own literal wording. Answer it using the context above and/or anything " +
              "you already said earlier in this conversation — summarizing, combining, translating, " +
              "or rephrasing either is fine, but don't add any new fact that isn't in one of the two. " +
              $"Only reply with exactly \"{GroundedAnswer.NoAnswerSentence}\" if neither the context " +
              "above nor anything you've already said covers that restated question."
            : "Answer using the context above and/or anything you already said earlier in this " +
              "conversation — summarizing, combining, translating, or rephrasing either is fine, " +
              "but don't add any new fact that isn't in one of the two. If neither covers the " +
              $"current question, reply with exactly: \"{GroundedAnswer.NoAnswerSentence}\"";

        var userMessage = $"Context:\n{context}\n{instructionText}\n\nQuestion: {currentMessage}";

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        return messages;
    }

    /// <summary>
    /// Re-surfaces every earlier User turn's actual attachment content
    /// (<see cref="ChatMessageEntry.AttachmentContent"/>) from THIS SAME
    /// session — see the class doc comment's "Sticky attachment memory"
    /// section for why this exists. Walks <paramref name="priorMessages"/>
    /// newest-first so a session with several attachments favors keeping
    /// the most recent ones intact within <see cref="MaxStickyAttachmentChars"/>,
    /// rather than filling the budget with the oldest and truncating
    /// exactly the material a follow-up is most likely to be about.
    /// </summary>
    private static string BuildStickyAttachmentsBlock(IReadOnlyList<ChatMessageEntry> priorMessages)
    {
        var sb = new StringBuilder();
        var remaining = MaxStickyAttachmentChars;

        for (var i = priorMessages.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var entry = priorMessages[i];
            if (entry.Role != ChatMessageRole.User || string.IsNullOrEmpty(entry.AttachmentContent))
            {
                continue;
            }

            var label = entry.AttachmentLabel is { Length: > 0 } l ? l : "an earlier attachment";
            var content = entry.AttachmentContent.Length > remaining
                ? entry.AttachmentContent[..remaining] + "…"
                : entry.AttachmentContent;

            // Inserted at the front, not appended — walking newest-first
            // but building the block in chronological (oldest-first)
            // reading order is easier for the model to follow than
            // reverse-chronological.
            sb.Insert(0, $"Previously attached ({label}): {content}\n\n");
            remaining -= content.Length;
        }

        return sb.ToString();
    }
}
