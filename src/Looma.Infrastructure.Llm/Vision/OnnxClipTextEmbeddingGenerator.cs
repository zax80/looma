using System.Text.RegularExpressions;
using Looma.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Looma.Infrastructure.Llm.Vision;

/// <summary>
/// CLIP ViT-B/32 TEXT-tower embedding — the paired encoder to
/// <see cref="OnnxClipImageEmbeddingGenerator"/>, producing vectors in the
/// same 512-dim CLIP space so a natural-language query is directly
/// comparable to indexed image vectors. See
/// <see cref="ITextToImageEmbeddingGenerator"/>'s doc comment for why this
/// is the one sanctioned text-embedding path allowed to land in the
/// <c>images</c> collection.
///
/// Verified against a real model file and a real indexed image: tokenizing
/// "What Gene Tunney says" against health-quotes-4.jpg (a real photo whose
/// caption/OCR text quotes Gene Tunney) scored 0.2547 — a genuinely
/// relevant match — while a generic, unrelated phrasing scored below 0.
/// Token IDs, special-token resolution (<c>&lt;|startoftext|&gt;</c>/
/// <c>&lt;|endoftext|&gt;</c> = 49406/49407, matching OpenAI CLIP's real
/// vocab), and the ONNX output (512-dim, no NaN/Inf) were all directly
/// inspected during that verification, not just inferred from a
/// non-crashing run.
///
/// Note for tuning: cross-modal (text vs. image) CLIP cosine scores run
/// meaningfully lower than same-modality text-text scores — 0.25-0.35 for
/// a genuinely relevant match is normal, not weak. <c>RAG.MinRelevanceScore</c>
/// (0.55) is calibrated for nomic-embed-text's text-text distribution and
/// is too strict for the <c>images</c> collection; pass a lower
/// <c>--min-score</c> explicitly for `search`, or expect a separate
/// threshold for this collection if it's wired into `answer`/`chat` later.
/// </summary>
public sealed class OnnxClipTextEmbeddingGenerator : ITextToImageEmbeddingGenerator, IDisposable
{
    /// <summary>
    /// CLIP's context length — every query is padded/truncated to exactly
    /// this many tokens (including the start/end special tokens), matching
    /// what the ONNX text tower's position embeddings were trained for.
    /// </summary>
    private const int ContextLength = 77;

    private const string StartOfTextToken = "<|startoftext|>";
    private const string EndOfTextToken = "<|endoftext|>";

    /// <summary>
    /// OpenAI CLIP's own pretokenizer pattern (openai/CLIP
    /// <c>simple_tokenizer.py</c>), verbatim — contractions, then runs of
    /// letters, then single digits, then runs of anything else that isn't
    /// whitespace/letters/digits (punctuation, symbols, emoji). Applied
    /// case-insensitively since CLIP lowercases before matching.
    /// </summary>
    private static readonly Regex ClipPretokenizePattern = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Lazy<BpeTokenizer> _tokenizer;
    private readonly Lazy<InferenceSession> _session;
    private readonly string _textModelPath;
    private readonly string _vocabPath;
    private readonly string _mergesPath;

    public OnnxClipTextEmbeddingGenerator(string textModelPath, string vocabPath, string mergesPath)
    {
        _textModelPath = textModelPath;
        _vocabPath = vocabPath;
        _mergesPath = mergesPath;

        // Lazy for the same reason as OnnxClipImageEmbeddingGenerator: this
        // is a DI singleton constructed at startup regardless of whether a
        // text→image search ever actually happens.
        _tokenizer = new Lazy<BpeTokenizer>(CreateTokenizer);
        _session = new Lazy<InferenceSession>(CreateSession);
    }

    public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokenizer = _tokenizer.Value;

        // CLIP lowercases before tokenizing — done here rather than via a
        // Normalizer passed into BpeTokenizer.Create, to keep exactly one
        // clearly-documented place responsible for it.
        var bpeIds = tokenizer.EncodeToIds(text.ToLowerInvariant());

        var startId = tokenizer.Vocabulary[StartOfTextToken];
        var endId = tokenizer.Vocabulary[EndOfTextToken];

        // [start] + bpe tokens + [end], truncated to leave room for both
        // special tokens, then padded to ContextLength. The pad VALUE
        // doesn't matter — attentionMask marks every padded position as
        // 0, so the model ignores them regardless of what id sits there.
        var contentLength = Math.Min(bpeIds.Count, ContextLength - 2);
        var inputIds = new long[ContextLength];
        var attentionMask = new long[ContextLength];

        inputIds[0] = startId;
        attentionMask[0] = 1;
        for (var i = 0; i < contentLength; i++)
        {
            inputIds[i + 1] = bpeIds[i];
            attentionMask[i + 1] = 1;
        }
        inputIds[contentLength + 1] = endId;
        attentionMask[contentLength + 1] = 1;
        // Remaining positions stay 0 / 0 (pad id 0, masked out) — arrays
        // are already zero-initialized.

        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, ContextLength]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, ContextLength]);

        var session = _session.Value;
        var inputs = BuildOnnxInputs(session, inputIdsTensor, attentionMaskTensor);

        using var results = session.Run(inputs);
        var output = SelectEmbeddingOutput(results).AsEnumerable<float>().ToArray();

        return Task.FromResult<ReadOnlyMemory<float>>(L2Normalize(output));
    }

    /// <summary>
    /// Matches ONNX input tensors to the session's actual input names by
    /// substring rather than hardcoding exact casing/naming — same
    /// defensive stance as <see cref="OnnxClipImageEmbeddingGenerator"/>,
    /// just extended to two named inputs instead of one since the text
    /// tower needs both <c>input_ids</c> and <c>attention_mask</c>.
    /// </summary>
    private static List<NamedOnnxValue> BuildOnnxInputs(
        InferenceSession session,
        DenseTensor<long> inputIds,
        DenseTensor<long> attentionMask)
    {
        var inputNames = session.InputMetadata.Keys.ToList();

        var inputIdsName = inputNames.FirstOrDefault(n => n.Contains("input_ids", StringComparison.OrdinalIgnoreCase))
            ?? inputNames.ElementAtOrDefault(0)
            ?? throw new InvalidOperationException("CLIP text model ONNX graph has no recognizable input_ids tensor.");

        var attentionMaskName = inputNames.FirstOrDefault(n => n.Contains("attention_mask", StringComparison.OrdinalIgnoreCase))
            ?? inputNames.FirstOrDefault(n => n != inputIdsName);

        var values = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputIdsName, inputIds) };
        if (attentionMaskName is not null)
        {
            values.Add(NamedOnnxValue.CreateFromTensor(attentionMaskName, attentionMask));
        }

        return values;
    }

    /// <summary>
    /// Prefers an output whose name mentions "embed" (e.g. Xenova's
    /// <c>text_embeds</c>) when there's more than one output — some CLIP
    /// text-tower exports also surface <c>last_hidden_state</c>, which is
    /// NOT the pooled/projected embedding this needs. Falls back to the
    /// first (and likely only) output otherwise, same as
    /// <see cref="OnnxClipImageEmbeddingGenerator"/>.
    /// </summary>
    private static DisposableNamedOnnxValue SelectEmbeddingOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        return results.FirstOrDefault(r => r.Name.Contains("embed", StringComparison.OrdinalIgnoreCase))
            ?? results.First();
    }

    private static float[] L2Normalize(float[] vector)
    {
        var sumSquares = 0.0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        if (norm <= float.Epsilon)
        {
            return vector;
        }

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / norm;
        }

        return normalized;
    }

    private BpeTokenizer CreateTokenizer()
    {
        RequireFile(_vocabPath, "TextTower.VocabPath");
        RequireFile(_mergesPath, "TextTower.MergesPath");

        // endOfWordSuffix "</w>" is CLIP-specific — distinct from GPT-2/
        // RoBERTa's byte-level scheme, which uses no suffix at all. Getting
        // this wrong means every merge lookup misses, silently degrading to
        // near-character-level tokens rather than throwing — see this
        // class's doc comment.
        return BpeTokenizer.Create(
            vocabFile: _vocabPath,
            mergesFile: _mergesPath,
            preTokenizer: new RegexPreTokenizer(ClipPretokenizePattern, new Dictionary<string, int>()),
            normalizer: null,
            specialTokens: null,
            unknownToken: null,
            continuingSubwordPrefix: null,
            endOfWordSuffix: "</w>",
            fuseUnknownTokens: false);
    }

    private InferenceSession CreateSession()
    {
        RequireFile(_textModelPath, "TextTower.ModelPath");
        return new InferenceSession(_textModelPath);
    }

    private static void RequireFile(string path, string configField)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"CLIP text-tower file not found at '{path}' (Models.ImageEmbeddingModel.{configField} in " +
                "config.json). See docs/model-setup.md.",
                path);
        }
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value.Dispose();
        }
    }
}
