using System.Runtime.CompilerServices;
using Looma.Application.Chunking;
using Looma.Application.Configuration;
using Looma.Application.Extraction;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Looma.Application.UseCases;

/// <summary>
/// Three ingestion pipelines, per the brief's "Ingestion Pipeline by Media
/// Type" table:
///
/// - Text (.txt/.md/.csv/.pdf/.docx/.xlsx, via <see cref="DocumentTextExtractor"/>):
///   chunk → embed (text) → <c>documents</c>.
/// - Image (.png/.jpg/.jpeg, via <see cref="ImageFile"/>): caption + OCR
///   (vision-language model) → chunk → embed (text) → <c>documents</c>,
///   *and in parallel* CLIP-embed → <c>images</c>.
/// - Audio (.wav/.mp3, via <see cref="AudioFile"/>): transcribe (Whisper,
///   local) → chunk with timestamp ranges → embed (text) → <c>documents</c>.
///
/// Anything else config.json's RAG.Sources[].FileTypes might list is
/// reported as <see cref="IndexingStatus.Skipped"/> with an explanation —
/// never silently ignored.
/// </summary>
public sealed class IndexingUseCase : IIndexingUseCase
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IImageCaptioner _imageCaptioner;
    private readonly IImageEmbeddingGenerator _imageEmbeddingGenerator;
    private readonly IAudioTranscriber _audioTranscriber;
    private readonly RagOptions _ragOptions;
    private readonly EmbeddingModelOptions _embeddingModelOptions;
    private readonly ImageEmbeddingModelOptions _imageEmbeddingModelOptions;

    public IndexingUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IImageCaptioner imageCaptioner,
        IImageEmbeddingGenerator imageEmbeddingGenerator,
        IAudioTranscriber audioTranscriber,
        IOptions<RagOptions> ragOptions,
        IOptions<EmbeddingModelOptions> embeddingModelOptions,
        IOptions<ImageEmbeddingModelOptions> imageEmbeddingModelOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _imageCaptioner = imageCaptioner;
        _imageEmbeddingGenerator = imageEmbeddingGenerator;
        _audioTranscriber = audioTranscriber;
        _ragOptions = ragOptions.Value;
        _embeddingModelOptions = embeddingModelOptions.Value;
        _imageEmbeddingModelOptions = imageEmbeddingModelOptions.Value;
    }

    /// <summary>
    /// Upper bound on how many chunks go into one embedding API call — the
    /// batching in <see cref="GenerateEmbeddingsAsync"/> is what actually
    /// speeds indexing up; this cap just guards against one enormous
    /// request for a single pathologically large file, same "blunt but
    /// simple safeguard" spirit as MainPage's MaxAttachedDocumentChars.
    /// </summary>
    private const int EmbeddingBatchSize = 32;

    /// <summary>
    /// Embeds every string in <paramref name="texts"/> in batches instead
    /// of one call per chunk. This replaced a real, measured inefficiency:
    /// the original version of this method made one sequential embedding
    /// round-trip per chunk, so a document with (say) 50 chunks made 50
    /// separate calls to the embedding model. Microsoft.Extensions.AI's
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> already
    /// supports embedding several inputs in one call
    /// (<c>GenerateAsync(IEnumerable&lt;TInput&gt;, ...)</c>) — same
    /// embeddings either way, just computed together. Returned in the same
    /// order as <paramref name="texts"/> (<c>GeneratedEmbeddings&lt;T&gt;</c>
    /// preserves input order).
    /// </summary>
    private async Task<List<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var results = new List<ReadOnlyMemory<float>>(texts.Count);

        for (var offset = 0; offset < texts.Count; offset += EmbeddingBatchSize)
        {
            var batch = texts.Skip(offset).Take(EmbeddingBatchSize).ToList();
            var embeddings = await _embeddingGenerator
                .GenerateAsync(batch, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            foreach (var embedding in embeddings)
            {
                results.Add(embedding.Vector);
            }
        }

        return results;
    }

    public async IAsyncEnumerable<IndexingProgress> IndexAsync(
        string path,
        bool recursive = true,
        bool clearFirst = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (clearFirst)
        {
            await _vectorStore.ClearCollectionAsync(VectorCollection.Documents, cancellationToken).ConfigureAwait(false);
            await _vectorStore.ClearCollectionAsync(VectorCollection.Images, cancellationToken).ConfigureAwait(false);
        }

        await _vectorStore.EnsureCollectionAsync(VectorCollection.Documents, _embeddingModelOptions.Dimensions, cancellationToken)
            .ConfigureAwait(false);
        await _vectorStore.EnsureCollectionAsync(VectorCollection.Images, _imageEmbeddingModelOptions.Dimensions, cancellationToken)
            .ConfigureAwait(false);

        var files = DiscoverFiles(path, recursive);
        var total = files.Count;

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            IndexingProgress progress;
            try
            {
                progress = await IndexFileAsync(file, i, total, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A single bad file shouldn't abort the whole run — report it and move on. The
                // exception message only, never file contents, per CLAUDE.md's logging constraint.
                progress = new IndexingProgress
                {
                    FilePath = file,
                    Status = IndexingStatus.Failed,
                    FileIndex = i,
                    TotalFiles = total,
                    ErrorMessage = ex.Message
                };
            }

            yield return progress;
        }
    }

    private static List<string> DiscoverFiles(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            return [path];
        }

        if (!Directory.Exists(path))
        {
            return [];
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(path, "*", searchOption)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private Task<IndexingProgress> IndexFileAsync(string file, int fileIndex, int total, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file);

        if (DocumentTextExtractor.IsSupported(extension))
        {
            return IndexTextFileAsync(file, fileIndex, total, cancellationToken);
        }

        if (ImageFile.IsSupported(extension))
        {
            return IndexImageFileAsync(file, fileIndex, total, cancellationToken);
        }

        if (AudioFile.IsSupported(extension))
        {
            return IndexAudioFileAsync(file, fileIndex, total, cancellationToken);
        }

        var supported = DocumentTextExtractor.SupportedExtensions
            .Concat(ImageFile.SupportedExtensions)
            .Concat(AudioFile.SupportedExtensions);
        return Task.FromResult(new IndexingProgress
        {
            FilePath = file,
            Status = IndexingStatus.Skipped,
            FileIndex = fileIndex,
            TotalFiles = total,
            ErrorMessage = $"'{extension}' ingestion isn't implemented yet in this milestone " +
                            $"(supported: {string.Join(", ", supported)}). " +
                            "See the ingestion pipeline table in docs/looma-project-brief.md."
        });
    }

    private async Task<IndexingProgress> IndexTextFileAsync(string file, int fileIndex, int total, CancellationToken cancellationToken)
    {
        var text = await DocumentTextExtractor.ExtractAsync(file, cancellationToken).ConfigureAwait(false);
        var chunks = TextChunker.Chunk(text, _ragOptions.ChunkSize, _ragOptions.ChunkOverlap);
        var embeddings = await GenerateEmbeddingsAsync(chunks.Select(c => c.Content).ToList(), cancellationToken)
            .ConfigureAwait(false);

        var indexedAtUtc = DateTimeOffset.UtcNow;
        var records = new List<VectorRecord>(chunks.Count);

        for (var idx = 0; idx < chunks.Count; idx++)
        {
            var chunk = chunks[idx];
            records.Add(new VectorRecord
            {
                Id = ChunkIdGenerator.Generate(file, chunk.Index),
                Embedding = embeddings[idx],
                Content = chunk.Content,
                Metadata = new ChunkMetadata
                {
                    SourcePath = file,
                    MediaType = MediaType.Text,
                    ChunkIndex = chunk.Index,
                    StartLine = chunk.StartLine,
                    EndLine = chunk.EndLine,
                    IndexedAtUtc = indexedAtUtc
                }
            });
        }

        if (records.Count > 0)
        {
            await _vectorStore.UpsertAsync(VectorCollection.Documents, records, cancellationToken).ConfigureAwait(false);
        }

        return new IndexingProgress
        {
            FilePath = file,
            Status = IndexingStatus.Completed,
            ChunksIndexed = records.Count,
            FileIndex = fileIndex,
            TotalFiles = total
        };
    }

    /// <summary>
    /// Both halves of the brief's "image" row: (a) caption + OCR, chunked
    /// and text-embedded into <c>documents</c> just like any other text —
    /// this is what makes <c>looma answer</c> able to surface image content
    /// at all without any change to the answer path; and (b) a CLIP vector
    /// into <c>images</c>, for image-similarity search once that's wired up
    /// on the query side (not yet — this milestone is ingestion only).
    /// </summary>
    private async Task<IndexingProgress> IndexImageFileAsync(string file, int fileIndex, int total, CancellationToken cancellationToken)
    {
        var indexedAtUtc = DateTimeOffset.UtcNow;

        ImageCaptionResult caption;
        await using (var captionStream = File.OpenRead(file))
        {
            caption = await _imageCaptioner.CaptionAsync(captionStream, cancellationToken).ConfigureAwait(false);
        }

        var captionText = string.IsNullOrWhiteSpace(caption.OcrText)
            ? caption.Caption
            : $"{caption.Caption}\n\nText visible in the image:\n{caption.OcrText}";

        var chunks = TextChunker.Chunk(captionText, _ragOptions.ChunkSize, _ragOptions.ChunkOverlap);
        var chunkEmbeddings = await GenerateEmbeddingsAsync(chunks.Select(c => c.Content).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var textRecords = new List<VectorRecord>(chunks.Count);

        for (var idx = 0; idx < chunks.Count; idx++)
        {
            var chunk = chunks[idx];
            textRecords.Add(new VectorRecord
            {
                Id = ChunkIdGenerator.Generate(file, chunk.Index),
                Embedding = chunkEmbeddings[idx],
                Content = chunk.Content,
                Metadata = new ChunkMetadata
                {
                    SourcePath = file,
                    MediaType = MediaType.Image,
                    ChunkIndex = chunk.Index,
                    IndexedAtUtc = indexedAtUtc
                }
            });
        }

        if (textRecords.Count > 0)
        {
            await _vectorStore.UpsertAsync(VectorCollection.Documents, textRecords, cancellationToken).ConfigureAwait(false);
        }

        ReadOnlyMemory<float> clipEmbedding;
        await using (var clipStream = File.OpenRead(file))
        {
            clipEmbedding = await _imageEmbeddingGenerator.EmbedAsync(clipStream, cancellationToken).ConfigureAwait(false);
        }

        var clipRecord = new VectorRecord
        {
            // Chunk index 0 in a distinct collection namespace from `documents`
            // — no collision risk with the text chunk ids above.
            Id = ChunkIdGenerator.Generate(file, 0),
            Embedding = clipEmbedding,
            Content = caption.Caption,
            Metadata = new ChunkMetadata
            {
                SourcePath = file,
                MediaType = MediaType.Image,
                ChunkIndex = 0,
                IndexedAtUtc = indexedAtUtc
            }
        };
        await _vectorStore.UpsertAsync(VectorCollection.Images, [clipRecord], cancellationToken).ConfigureAwait(false);

        return new IndexingProgress
        {
            FilePath = file,
            Status = IndexingStatus.Completed,
            ChunksIndexed = textRecords.Count + 1,
            FileIndex = fileIndex,
            TotalFiles = total
        };
    }

    /// <summary>
    /// The brief's "audio" row: transcribe, then chunk with timestamp
    /// ranges instead of line ranges (<see cref="TranscriptChunker"/>,
    /// tracked via <see cref="ChunkMetadata.StartTime"/>/<see cref="ChunkMetadata.EndTime"/>),
    /// then embed and store into <c>documents</c> exactly like any other
    /// text — same reasoning as image captions: this is what makes
    /// <c>looma answer</c> able to surface audio content with zero changes
    /// to the answer path.
    /// </summary>
    private async Task<IndexingProgress> IndexAudioFileAsync(string file, int fileIndex, int total, CancellationToken cancellationToken)
    {
        var segments = new List<TranscriptSegment>();
        await using (var audioStream = File.OpenRead(file))
        {
            await foreach (var segment in _audioTranscriber.TranscribeAsync(audioStream, cancellationToken).ConfigureAwait(false))
            {
                segments.Add(segment);
            }
        }

        var chunks = TranscriptChunker.Chunk(segments, _ragOptions.ChunkSize, _ragOptions.ChunkOverlap);
        var embeddings = await GenerateEmbeddingsAsync(chunks.Select(c => c.Content).ToList(), cancellationToken)
            .ConfigureAwait(false);
        var indexedAtUtc = DateTimeOffset.UtcNow;
        var records = new List<VectorRecord>(chunks.Count);

        for (var idx = 0; idx < chunks.Count; idx++)
        {
            var chunk = chunks[idx];
            records.Add(new VectorRecord
            {
                Id = ChunkIdGenerator.Generate(file, chunk.Index),
                Embedding = embeddings[idx],
                Content = chunk.Content,
                Metadata = new ChunkMetadata
                {
                    SourcePath = file,
                    MediaType = MediaType.Audio,
                    ChunkIndex = chunk.Index,
                    StartTime = chunk.StartTime,
                    EndTime = chunk.EndTime,
                    IndexedAtUtc = indexedAtUtc
                }
            });
        }

        if (records.Count > 0)
        {
            await _vectorStore.UpsertAsync(VectorCollection.Documents, records, cancellationToken).ConfigureAwait(false);
        }

        return new IndexingProgress
        {
            FilePath = file,
            Status = IndexingStatus.Completed,
            ChunksIndexed = records.Count,
            FileIndex = fileIndex,
            TotalFiles = total
        };
    }
}
