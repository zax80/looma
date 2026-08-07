using Looma.Infrastructure.Llm;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

/// <summary>
/// Only the deterministic, no-network branches — actually downloading a
/// file needs a real (or mocked) HTTP endpoint, which isn't worth building
/// out here; the download path gets its real verification from an actual
/// `looma` run against the real internet, same as Ollama's pull path.
/// Covers both CLIP and Whisper provisioning, since both go through the
/// same generic <see cref="LocalModelFileProvisioner.EnsureFileAsync"/>.
/// </summary>
public sealed class LocalModelFileProvisionerTests
{
    [Fact]
    public async Task EnsureFileAsync_FileAlreadyExists_ReturnsWithoutTouchingNetwork()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // downloadUrl is garbage on purpose — if this method tried to
            // use it, it would throw, so a clean return proves the
            // already-exists short-circuit ran instead.
            await LocalModelFileProvisioner.EnsureFileAsync(
                tempFile, downloadUrl: "not a real url", onStatus: null, CancellationToken.None);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EnsureFileAsync_MissingFileNoDownloadUrl_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            LocalModelFileProvisioner.EnsureFileAsync(missingPath, downloadUrl: null, onStatus: null, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureFileAsync_NoModelPathConfigured_ReturnsWithoutError(string? modelPath)
    {
        // No path configured at all is treated as "nothing to provision",
        // not an error — matches OnnxClipImageEmbeddingGenerator/
        // WhisperAudioTranscriber only being constructed when a path IS
        // configured (see AddLoomaImageEmbeddingGenerator / AddLoomaAudioTranscriber).
        await LocalModelFileProvisioner.EnsureFileAsync(modelPath, downloadUrl: null, onStatus: null, CancellationToken.None);
    }
}
