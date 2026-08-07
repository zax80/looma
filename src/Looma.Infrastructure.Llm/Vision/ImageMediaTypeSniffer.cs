namespace Looma.Infrastructure.Llm.Vision;

/// <summary>
/// Detects the real image MIME type from magic bytes rather than trusting a
/// file extension — the vision model's multimodal content part needs an
/// accurate MIME type, and a renamed/mislabeled file would otherwise get a
/// wrong one silently. Falls back to the extension only when the bytes
/// aren't recognized.
/// </summary>
public static class ImageMediaTypeSniffer
{
    public static string Detect(ReadOnlySpan<byte> bytes, string fallbackExtension)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        return fallbackExtension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new NotSupportedException(
                $"Could not determine an image MIME type from content or extension '{fallbackExtension}'.")
        };
    }
}
