using System.Text.Json;
using System.Text.Json.Serialization;

namespace Looma.Infrastructure.LocalStore;

/// <summary>
/// Shared JSON options for both file stores in this project — string enums
/// (so ChatMessageRole reads as "User"/"Assistant" in the file, not 0/1)
/// and indented output for a human-readable local file, same as
/// QdrantAnswerCache's exact-match layer.
/// </summary>
internal static class LocalStoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
