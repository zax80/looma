namespace Looma.Application;

/// <summary>
/// The exact sentence <see cref="UseCases.ChatCompletionUseCase"/>'s and
/// <see cref="UseCases.AnswerUseCase"/>'s system prompts instruct the model
/// to reply with verbatim when neither the retrieved context nor (for chat)
/// anything already said in the conversation covers the question — see each
/// use case's own doc comment for the prompt-engineering reasoning behind
/// the exact wording.
///
/// Shared here instead of duplicated as a private const in each use case
/// (which is how it existed before this type — a real, if harmless, bit of
/// drift risk) specifically so a consumer outside
/// <c>Looma.Application.UseCases</c> — <c>MainPage</c>, deciding whether a
/// finished answer is substantive enough to offer a document export for —
/// can recognize a refusal without re-deriving or re-hardcoding the
/// sentence itself. Offering to export "The provided context does not
/// contain this information." as if it were real content is exactly the
/// bug this exists to prevent.
/// </summary>
public static class GroundedAnswer
{
    public const string NoAnswerSentence = "The provided context does not contain this information.";
}
