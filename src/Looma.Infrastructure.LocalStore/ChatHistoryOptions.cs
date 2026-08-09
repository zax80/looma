namespace Looma.Infrastructure.LocalStore;

/// <summary>Binds to the <c>ChatHistory</c> section of config.json.</summary>
public sealed class ChatHistoryOptions
{
    public const string SectionName = "ChatHistory";

    /// <summary>Resolved relative to the working directory, same convention as AnswerCache.FilePath.</summary>
    public string SessionsFilePath { get; set; } = "./.looma/chat-sessions.json";

    public string SavedAnswersFilePath { get; set; } = "./.looma/saved-answers.json";
}
