# Local model setup

Everything is auto-provisioned on first run now — nothing to do manually in
the normal case:

- **Chat, embedding, vision (captioning)** — `Models.BaseModel`,
  `Models.EmbeddingModel`, `Models.VisionModel` (`qwen2.5vl:7b` by default)
  are pulled through Ollama by `OllamaStartup`. BaseModel/EmbeddingModel
  failures are fatal — every RAG operation genuinely needs both. VisionModel
  failures are **not** fatal (see `OllamaStartup`'s doc comment) — only the
  image-captioning path needs it, so a failed pull logs a warning and lets
  everything else proceed.
- **CLIP (image embeddings)** — `Models.ImageEmbeddingModel`
  (`./models/clip-vit-b32.onnx` by default) is downloaded directly via HTTP
  by `LocalModelFileProvisioner` from `Models.ImageEmbeddingModel.DownloadUrl`
  if the file doesn't already exist. Not fatal on failure — only the image
  half of ingestion needs it. `OnnxClipImageEmbeddingGenerator` fails loudly
  and specifically if a run that actually needs the file finds it missing.
- **CLIP text tower (text→image search)** — `Models.ImageEmbeddingModel.TextTower`
  (three files: an ONNX text encoder plus its tokenizer's vocab/merges) is
  entirely optional, downloaded the same way if configured at all. See
  "Text→image search" below.
- **Whisper (audio transcription)** — `Models.SpeechToTextModel`
  (`./models/whisper-base.bin` by default) is downloaded the same way, from
  `Models.SpeechToTextModel.DownloadUrl`. Not fatal on failure either — only
  the audio half of ingestion needs it. `WhisperAudioTranscriber` fails
  loudly and specifically if a run that actually needs the file finds it
  missing.

All of this is a direct implementation of the brief's model-provisioning
design (docs/looma-project-brief.md section 6): "auto-pulled on first run
... through Ollama for chat/embedding/vision, direct ONNX fetch for
Whisper/CLIP."

## Manual fallback — CLIP

If auto-download fails, or you want a different quantized variant, or
you're setting this up somewhere offline: download `vision_model.onnx` (or
`vision_model_quantized.onnx` for a smaller/faster file — accuracy
trade-off is small for this use case) from
<https://huggingface.co/Xenova/clip-vit-base-patch32/tree/main/onnx> and
save it as `./models/clip-vit-b32.onnx` (relative to wherever `config.json`
is loaded from). PowerShell:

```powershell
New-Item -ItemType Directory -Force -Path .\models | Out-Null
Invoke-WebRequest `
  -Uri "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx" `
  -OutFile ".\models\clip-vit-b32.onnx"
```

`OnnxClipImageEmbeddingGenerator` doesn't hardcode the ONNX graph's
input/output tensor names — it reads whatever single input and output the
session reports, so it should tolerate other CLIP ViT-B/32 vision-encoder
exports too, as long as:

- Input: a single `float32` tensor, NCHW layout, `[1, 3, 224, 224]`.
- Output: a single `float32` embedding vector — configured length is
  `Models.ImageEmbeddingModel.Dimensions` (512 by default; must match
  whatever the model actually outputs, or Qdrant will reject the upsert
  with a clear dimension-mismatch error, not silently misindex).

The preprocessing (`ClipImagePreprocessor`) resizes/center-crops to 224×224
and normalizes with CLIP's own mean/std
(`[0.48145466, 0.4578275, 0.40821073]` / `[0.26862954, 0.26130258, 0.27577711]`)
— the standard OpenAI/open_clip recipe, not ImageNet's. A vision-only
encoder (not the full CLIP model with the text tower) is all that's needed
for image ingestion; text→image query-side search needs the paired text
encoder too — see the next section.

## Text→image search

Optional — `looma search --collection images "<text query>"` (or the
`looma_search` MCP tool with `collection="images"`) needs CLIP's paired
TEXT encoder, not just the vision encoder image ingestion already uses.
Nothing downloads this by default; add `Models.ImageEmbeddingModel.TextTower`
to config.json to opt in:

```json
"ImageEmbeddingModel": {
  "Provider": "Local.OnnxClip",
  "ModelPath": "./models/clip-vit-b32.onnx",
  "DownloadUrl": "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx",
  "Dimensions": 512,
  "TextTower": {
    "ModelPath": "./models/clip-vit-b32-text.onnx",
    "DownloadUrl": "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/text_model.onnx",
    "VocabPath": "./models/clip-vocab.json",
    "VocabDownloadUrl": "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/vocab.json",
    "MergesPath": "./models/clip-merges.txt",
    "MergesDownloadUrl": "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/merges.txt"
  }
}
```

All three files (the text-encoder ONNX graph plus the tokenizer's
vocab.json/merges.txt) auto-download from the same Xenova/clip-vit-base-patch32
repo the vision model already comes from — about 250 MB total, mostly the
ONNX graph. Each is independently best-effort at startup, same as CLIP/
Whisper: a failed download logs a warning and only blocks
`--collection images` text search, nothing else. Without `TextTower`
configured, that same command fails with a clear "not configured" error
instead of Qdrant's confusing dimension-mismatch message.

Verified against a real model file and a real indexed image — see
`OnnxClipTextEmbeddingGenerator`'s doc comment for the specific query/score
that confirmed it. One tuning caveat found during that verification:
cross-modal (text-vs-image) CLIP scores run meaningfully lower than
`RAG.MinRelevanceScore`'s text-vs-text calibration — pass an explicit,
lower `--min-score` when using `search --collection images`, don't rely on
the configured default.

## Manual fallback — Whisper

Download a GGML model (e.g. `ggml-base.bin`, ~140 MB — bigger ones like
`ggml-small.bin`/`ggml-medium.bin` trade speed for accuracy) from
<https://huggingface.co/ggerganov/whisper.cpp/tree/main> and save it as
`./models/whisper-base.bin`:

```powershell
New-Item -ItemType Directory -Force -Path .\models | Out-Null
Invoke-WebRequest `
  -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin" `
  -OutFile ".\models\whisper-base.bin"
```

If you switch to a different-sized model, the file just needs to exist at
whatever `Models.SpeechToTextModel.ModelPath` points to — `WhisperAudioTranscriber`
doesn't care which GGML size it is.

### Audio format support

`.wav` and `.mp3` are both supported (`AudioFile.SupportedExtensions`).
Whisper itself only accepts 16kHz mono PCM WAV, so
`WhisperAudioTranscriber` normalizes whatever it's given (any sample rate,
mono or stereo) via NAudio before handing it to Whisper.net — same
resample/downmix approach as Whisper.net's own published examples
(`NAudioResampleWav`, `NAudioMp3`).

**Known platform caveat:** MP3 decoding goes through `NAudio.Wave.Mp3FileReader`,
which relies on the OS's own media framework (Media Foundation on Windows).
This is verified working on Windows; behavior on Linux/macOS is unverified
and may not work without that platform's equivalent codec support. WAV
decoding doesn't have this dependency and should work everywhere.

## Not yet verified end-to-end

CLIP (vision tower) was verified against a real model file and real image
in this session — captioning, chunking, embedding, retrieval, and grounded
answers all confirmed working. Whisper/audio has been built and
unit-tested (chunking logic, format sniffing, the deterministic
provisioning branches) but not yet run against a real GGML model and real
audio file — that first real `looma index` run against a `.wav`/`.mp3`
file is the actual verification step, same as CLIP got.

CLIP's TEXT tower (text→image search, see above) has now also been
verified against a real model file and a real indexed image — see
`OnnxClipTextEmbeddingGenerator`'s doc comment for the specific query/score
that confirmed it. Note the cross-modal scoring caveat there: CLIP
text-vs-image scores run lower than `RAG.MinRelevanceScore` was calibrated
for (text-vs-text), so pass an explicit `--min-score` well below 0.55 when
using `search --collection images`.
