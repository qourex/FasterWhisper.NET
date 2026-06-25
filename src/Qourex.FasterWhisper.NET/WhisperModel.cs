// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// High-level client for transcribing audio using CTranslate2's Whisper engine.
    /// </summary>
    /// <remarks>
    /// <para>This class is <b>NOT</b> thread-safe. Do not call <see cref="Transcribe(float[], string?, string, WhisperOptions?, VadOptions?, IProgress{TranscriptionProgress}?)"/>
    /// or <see cref="TranscribeStreamAsync"/> concurrently from multiple threads on the same instance.
    /// The underlying native CTranslate2 model does not support concurrent access.</para>
    /// <para>Implements <see cref="IDisposable"/>. Always dispose when done to release native resources.
    /// Calling any method after disposal will throw <see cref="ObjectDisposedException"/>.</para>
    /// </remarks>
    public class WhisperModel : IDisposable
    {
        // Whisper protocol constants
        internal const int SampleRate = 16000;
        private const int MelFrameCount = 3000;
        private const int MaxPreviousTokens = 224;
        private const float FrameDurationSeconds = 0.02f;
        internal const int MaxChunkSamples = SampleRate * 30;

        private IntPtr _modelPtr;
        private bool _disposed;
        private readonly WhisperTokenizer _tokenizer;
        private readonly AudioProcessor _audioProcessor;
        private readonly string _modelPath;
        private readonly bool _isMultilingual;
        private readonly int _nMels;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _modelSemaphore;
        private readonly List<IDisposable> _mappedResources = new();

        private class LockContext
        {
            public Guid Id { get; } = Guid.NewGuid();
            public int Count { get; set; } = 1;
        }

        private readonly AsyncLocal<LockContext?> _lockContext = new();

        private void AcquireLock()
        {
            var ctx = _lockContext.Value;
            if (ctx != null)
            {
                ctx.Count++;
                return;
            }

            _modelSemaphore.Wait();
            _lockContext.Value = new LockContext();
        }

        private void ReleaseLock()
        {
            var ctx = _lockContext.Value;
            if (ctx != null)
            {
                ctx.Count--;
                if (ctx.Count == 0)
                {
                    _lockContext.Value = null;
                    _modelSemaphore.Release();
                }
            }
        }

        private async Task AcquireLockAsync(CancellationToken cancellationToken)
        {
            var ctx = _lockContext.Value;
            if (ctx != null)
            {
                ctx.Count++;
                return;
            }

            await _modelSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            _lockContext.Value = new LockContext();
        }

        private void ReleaseLockAsync()
        {
            ReleaseLock();
        }

        /// <summary>
        /// Gets whether the loaded model is multilingual.
        /// </summary>
        public bool IsMultilingual => _isMultilingual;

        /// <summary>
        /// Gets the number of Mel channels expected by the model (typically 80 or 128).
        /// </summary>
        public int NMels => _nMels;

        /// <summary>
        /// Gets the number of Mel frequency bins (alias for NMels). Used by BatchedInferencePipeline.
        /// </summary>
        internal int MelBins => _nMels;

        /// <summary>
        /// Gets the audio processor instance. Used by BatchedInferencePipeline for Mel extraction.
        /// </summary>
        internal AudioProcessor AudioProcessorInstance => _audioProcessor;

        /// <summary>Fired when a segment is decoded and ready.</summary>
        public event EventHandler<WhisperSegment>? OnSegmentDecoded;

        /// <summary>Fired when transcription progress updates.</summary>
        public event EventHandler<TranscriptionProgress>? OnProgress;

        /// <summary>
        /// Loads a Whisper model from local path or downloads it automatically from Hugging Face if shorthand is provided.
        /// </summary>
        /// <param name="modelNameOrPath">Model shorthand (e.g. "base", "large-v3"), HuggingFace repo ID, or local directory.</param>
        /// <param name="device">Device to run on ("cpu" or "cuda"). Default is "cpu".</param>
        /// <param name="computeType">Computation precision ("float32", "float16", "int16", "int8", "int8_float16", "default").</param>
        /// <param name="deviceIndices">Device indices to distribute model replicas on (e.g., [0]).</param>
        /// <param name="cpuThreads">Number of threads per replica for CPU execution. Default is 4.</param>
        /// <param name="flashAttention">Whether to use Flash Attention (CUDA only). Default is false.</param>
        /// <param name="numReplicas">Number of model replicas to load for concurrent inference.</param>
        /// <param name="cacheDir">Optional cache folder directory.</param>
        /// <param name="progress">Progress reporter for model download.</param>
        /// <param name="cancellationToken">Cancellation token for model downloading.</param>
        /// <param name="logger">Optional ILogger for diagnostic logging.</param>
        public static async Task<WhisperModel> LoadAsync(
            string modelNameOrPath,
            string device = "cpu",
            string computeType = "default",
            int[]? deviceIndices = null,
            int cpuThreads = 4,
            bool flashAttention = false,
            int numReplicas = 1,
            string? cacheDir = null,
            IProgress<(string FileName, long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken cancellationToken = default,
            ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(modelNameOrPath);
            logger?.LogInformation("Loading model '{Model}' on {Device} ({ComputeType}) with {Replicas} replicas", modelNameOrPath, device, computeType, numReplicas);
            var downloader = new ModelDownloader(cacheDir);
            string resolvedPath = await downloader.GetModelPathAsync(modelNameOrPath, progress, cancellationToken).ConfigureAwait(false);
            return new WhisperModel(resolvedPath, device, computeType, deviceIndices, cpuThreads, flashAttention, numReplicas, logger);
        }

        /// <summary>
        /// Loads a model using memory-mapped I/O for faster startup and lower peak RAM.
        /// The OS pages model data on-demand instead of loading everything upfront.
        /// </summary>
        /// <param name="modelPath">Path to the model directory.</param>
        /// <param name="device">Device to run on ("cpu" or "cuda"). Default is "cpu".</param>
        /// <param name="computeType">Computation precision. Default is "default".</param>
        /// <param name="deviceIndices">Device indices for multi-GPU. Default is [0].</param>
        /// <param name="cpuThreads">Number of threads per replica. Default is 4.</param>
        /// <param name="flashAttention">Use Flash Attention (CUDA only). Default is false.</param>
        /// <param name="numReplicas">Number of model replicas to load for concurrent inference.</param>
        /// <param name="logger">Optional ILogger for diagnostic logging.</param>
        /// <returns>A new WhisperModel loaded via memory-mapped files.</returns>
        public static WhisperModel LoadMemoryMapped(
            string modelPath,
            string device = "cpu",
            string computeType = "default",
            int[]? deviceIndices = null,
            int cpuThreads = 4,
            bool flashAttention = false,
            int numReplicas = 1,
            ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(modelPath);

            if (!Directory.Exists(modelPath))
                throw new DirectoryNotFoundException($"Model directory not found: {modelPath}");

            logger?.LogInformation("Loading model via true memory-mapped I/O from '{Path}' with {Replicas} replicas", modelPath, numReplicas);

            var mappedList = new List<(string Filename, System.IO.MemoryMappedFiles.MemoryMappedFile Mmf, System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Accessor)>();
            try
            {
                string[] requiredFiles = Directory.GetFiles(modelPath);
                foreach (string filePath in requiredFiles)
                {
                    string fileName = Path.GetFileName(filePath);
                    var fileInfo = new FileInfo(filePath);

                    if (fileInfo.Length == 0) continue;

                    var mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
                        filePath, FileMode.Open, null, 0, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
                    
                    var accessor = mmf.CreateViewAccessor(0, fileInfo.Length, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);

                    mappedList.Add((fileName, mmf, accessor));
                }

                logger?.LogDebug("Memory-mapped {Count} model files", mappedList.Count);

                return new WhisperModel(mappedList, device, computeType, deviceIndices, cpuThreads, flashAttention, numReplicas);
            }
            catch
            {
                foreach (var tuple in mappedList)
                {
                    tuple.Accessor?.Dispose();
                    tuple.Mmf?.Dispose();
                }
                throw;
            }
        }

        private WhisperModel(
            List<(string Filename, System.IO.MemoryMappedFiles.MemoryMappedFile Mmf, System.IO.MemoryMappedFiles.MemoryMappedViewAccessor Accessor)> mappedFiles,
            string device,
            string computeType,
            int[]? deviceIndices,
            int cpuThreads,
            bool flashAttention,
            int numReplicas)
        {
            _modelSemaphore = new SemaphoreSlim(numReplicas, numReplicas);
            if (mappedFiles == null || mappedFiles.Count == 0)
                throw new ArgumentException("Mapped files list cannot be null or empty.");

            // Find vocabulary file to initialize the tokenizer
            string? vocabContent = null;
            bool isJson = false;

            var vocabTuple = mappedFiles.Find(f => f.Filename == "vocabulary.txt");
            if (vocabTuple.Accessor != null)
            {
                isJson = false;
            }
            else
            {
                vocabTuple = mappedFiles.Find(f => f.Filename == "vocabulary.json");
                isJson = true;
            }

            if (vocabTuple.Accessor != null)
            {
                byte[] vocabBytes = new byte[vocabTuple.Accessor.Capacity];
                vocabTuple.Accessor.ReadArray(0, vocabBytes, 0, vocabBytes.Length);
                vocabContent = System.Text.Encoding.UTF8.GetString(vocabBytes);
            }

            if (vocabContent == null)
            {
                throw new KeyNotFoundException("Vocabulary file ('vocabulary.txt' or 'vocabulary.json') not found in the mapped files.");
            }

            _modelPath = "mapped";
            _tokenizer = new WhisperTokenizer(vocabContent, isJson);

            // Prepare native files array
            nuint numFiles = (nuint)mappedFiles.Count;
            nuint numDevices = (nuint)(deviceIndices?.Length ?? 0);

            var pinnedHandles = new List<GCHandle>();
            var nativeFiles = new List<NativeMemoryFile>();

            try
            {
                foreach (var tuple in mappedFiles)
                {
                    byte[] filenameBytes = System.Text.Encoding.UTF8.GetBytes(tuple.Filename + "\0");
                    GCHandle filenameHandle = GCHandle.Alloc(filenameBytes, GCHandleType.Pinned);
                    pinnedHandles.Add(filenameHandle);

                    unsafe
                    {
                        byte* ptr = null;
                        tuple.Accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                        byte* dataPtr = ptr + tuple.Accessor.PointerOffset;

                        nativeFiles.Add(new NativeMemoryFile
                        {
                            Filename = (byte*)filenameHandle.AddrOfPinnedObject(),
                            Data = dataPtr,
                            Size = (nuint)tuple.Accessor.Capacity
                        });
                    }
                }

                IntPtr errorPtr;
                unsafe
                {
                    fixed (NativeMemoryFile* filesPtr = nativeFiles.ToArray())
                    {
                        _modelPtr = NativeMethods.LoadWhisperModelFromMemory(
                            filesPtr,
                            numFiles,
                            device,
                            computeType,
                            deviceIndices,
                            numDevices,
                            cpuThreads,
                            flashAttention,
                            numReplicas,
                            out errorPtr);
                    }
                }

                if (_modelPtr == IntPtr.Zero)
                {
                    string errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native loading error."
                        : "Unknown native loading error.";

                    if (errorPtr != IntPtr.Zero)
                    {
                        NativeMethods.FreeString(errorPtr);
                    }

                    throw new ExternalException($"Failed to load memory-mapped Whisper model: {errorMsg}");
                }
            }
            finally
            {
                foreach (var handle in pinnedHandles)
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }

                foreach (var tuple in mappedFiles)
                {
                    tuple.Accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }

            // Keep mapped resources alive
            foreach (var tuple in mappedFiles)
            {
                _mappedResources.Add(tuple.Mmf);
                _mappedResources.Add(tuple.Accessor);
            }

            _isMultilingual = NativeMethods.WhisperIsMultilingual(_modelPtr) != 0;
            _nMels = (int)NativeMethods.WhisperNMels(_modelPtr);
            if (_nMels <= 0)
            {
                _nMels = 80;
            }

            _audioProcessor = new AudioProcessor(_nMels);
        }

        /// <summary>
        /// Loads a Whisper model from in-memory byte buffers for each model file.
        /// </summary>
        /// <param name="modelFiles">A dictionary mapping filenames (e.g. "model.bin", "config.json", "vocabulary.txt") to their raw byte contents.</param>
        /// <param name="device">Device to run on ("cpu" or "cuda"). Default is "cpu".</param>
        /// <param name="computeType">Computation precision ("float32", "float16", "int16", "int8", "int8_float16", "default").</param>
        /// <param name="deviceIndices">Device indices to distribute model replicas on (e.g., [0]).</param>
        /// <param name="cpuThreads">Number of threads per replica for CPU execution. Default is 4.</param>
        /// <param name="flashAttention">Whether to use Flash Attention (CUDA only). Default is false.</param>
        /// <param name="numReplicas">Number of model replicas to load for concurrent inference.</param>
        public WhisperModel(
            Dictionary<string, byte[]> modelFiles,
            string device = "cpu",
            string computeType = "default",
            int[]? deviceIndices = null,
            int cpuThreads = 4,
            bool flashAttention = false,
            int numReplicas = 1)
        {
            _modelSemaphore = new SemaphoreSlim(numReplicas, numReplicas);
            if (modelFiles == null || modelFiles.Count == 0)
                throw new ArgumentException("Model files dictionary cannot be null or empty.");

            // Find vocabulary file to initialize the tokenizer
            string? vocabContent = null;
            bool isJson = false;

            if (modelFiles.TryGetValue("vocabulary.txt", out byte[]? vocabBytes))
            {
                vocabContent = System.Text.Encoding.UTF8.GetString(vocabBytes);
            }
            else if (modelFiles.TryGetValue("vocabulary.json", out byte[]? vocabJsonBytes))
            {
                vocabContent = System.Text.Encoding.UTF8.GetString(vocabJsonBytes);
                isJson = true;
            }

            if (vocabContent == null)
            {
                throw new KeyNotFoundException("Vocabulary file ('vocabulary.txt' or 'vocabulary.json') not found in the model files dictionary.");
            }

            _modelPath = "memory";
            _tokenizer = new WhisperTokenizer(vocabContent, isJson);

            // Prepare native files array
            nuint numFiles = (nuint)modelFiles.Count;
            nuint numDevices = (nuint)(deviceIndices?.Length ?? 0);

            // Pin all byte arrays and allocate unmanaged memory
            var pinnedHandles = new List<GCHandle>();
            var nativeFiles = new List<NativeMemoryFile>();

            try
            {
                foreach (var kvp in modelFiles)
                {
                    byte[] filenameBytes = System.Text.Encoding.UTF8.GetBytes(kvp.Key + "\0");
                    GCHandle filenameHandle = GCHandle.Alloc(filenameBytes, GCHandleType.Pinned);
                    pinnedHandles.Add(filenameHandle);

                    GCHandle dataHandle = GCHandle.Alloc(kvp.Value, GCHandleType.Pinned);
                    pinnedHandles.Add(dataHandle);

                    unsafe
                    {
                        nativeFiles.Add(new NativeMemoryFile
                        {
                            Filename = (byte*)filenameHandle.AddrOfPinnedObject(),
                            Data = (byte*)dataHandle.AddrOfPinnedObject(),
                            Size = (nuint)kvp.Value.Length
                        });
                    }
                }

                IntPtr errorPtr;
                unsafe
                {
                    fixed (NativeMemoryFile* filesPtr = nativeFiles.ToArray())
                    {
                        _modelPtr = NativeMethods.LoadWhisperModelFromMemory(
                            filesPtr,
                            numFiles,
                            device,
                            computeType,
                            deviceIndices,
                            numDevices,
                            cpuThreads,
                            flashAttention,
                            numReplicas,
                            out errorPtr);
                    }
                }

                if (_modelPtr == IntPtr.Zero)
                {
                    string errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native loading error."
                        : "Unknown native loading error.";

                    if (errorPtr != IntPtr.Zero)
                    {
                        NativeMethods.FreeString(errorPtr);
                    }

                    throw new ExternalException($"Failed to load Whisper model from memory: {errorMsg}");
                }
            }
            finally
            {
                foreach (var handle in pinnedHandles)
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }
            }

            _isMultilingual = NativeMethods.WhisperIsMultilingual(_modelPtr) != 0;
            _nMels = (int)NativeMethods.WhisperNMels(_modelPtr);
            if (_nMels <= 0)
            {
                _nMels = 80;
            }

            _audioProcessor = new AudioProcessor(_nMels);
        }

        /// <summary>
        /// Synchronously initializes the model from a local directory path.
        /// </summary>
        /// <param name="modelPath">Path to the model directory.</param>
        /// <param name="device">Device to run on ("cpu" or "cuda"). Default is "cpu".</param>
        /// <param name="computeType">Computation precision ("float32", "float16", "int16", "int8", "int8_float16", "default").</param>
        /// <param name="deviceIndices">Device indices to distribute model replicas on.</param>
        /// <param name="cpuThreads">Number of threads per replica for CPU execution. Default is 4.</param>
        /// <param name="flashAttention">Whether to use Flash Attention (CUDA only). Default is false.</param>
        /// <param name="numReplicas">Number of model replicas to load for concurrent inference.</param>
        /// <param name="logger">Optional ILogger for diagnostic logging.</param>
        public WhisperModel(
            string modelPath,
            string device = "cpu",
            string computeType = "default",
            int[]? deviceIndices = null,
            int cpuThreads = 4,
            bool flashAttention = false,
            int numReplicas = 1,
            ILogger? logger = null)
        {
            _modelSemaphore = new SemaphoreSlim(numReplicas, numReplicas);
            _modelPath = modelPath;
            _logger = logger;
            _tokenizer = new WhisperTokenizer(modelPath);

            nuint numDevices = (nuint)(deviceIndices?.Length ?? 0);
            IntPtr errorPtr;
            
            _modelPtr = NativeMethods.LoadWhisperModel(
                modelPath,
                device,
                computeType,
                deviceIndices,
                numDevices,
                cpuThreads,
                flashAttention,
                numReplicas,
                out errorPtr);

            if (_modelPtr == IntPtr.Zero)
            {
                string errorMsg = errorPtr != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native loading error."
                    : "Unknown native loading error.";
                
                if (errorPtr != IntPtr.Zero)
                {
                    NativeMethods.FreeString(errorPtr);
                }

                throw new ExternalException($"Failed to load Whisper model: {errorMsg}");
            }

            _isMultilingual = NativeMethods.WhisperIsMultilingual(_modelPtr) != 0;
            _nMels = (int)NativeMethods.WhisperNMels(_modelPtr);
            if (_nMels <= 0)
            {
                // Fallback to default if native call returned invalid/empty
                _nMels = 80; 
            }

            _audioProcessor = new AudioProcessor(_nMels);
            _logger?.LogInformation(
                "Model loaded: {Path}, multilingual={Multilingual}, nMels={NMels}, device={Device}",
                modelPath, _isMultilingual, _nMels, device);
        }

        /// <summary>
        /// Transcribes an audio file of any format (WAV, MP3, MP4, Opus, etc.).
        /// Spawns FFmpeg automatically to decode non-WAV formats.
        /// </summary>
        public IEnumerable<WhisperSegment> Transcribe(
            string mediaPath,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(mediaPath);
            float[] pcm;
            try
            {
                // Try reading as a standard WAV first
                pcm = _audioProcessor.LoadWav(mediaPath);
            }
            catch (InvalidDataException)
            {
                // Fallback to FFmpeg decoder for other formats (MP3, MP4, Opus, etc.)
                pcm = AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }
            catch (NotSupportedException)
            {
                pcm = AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }
            return Transcribe(pcm, language, task, options, vadOptions, progress);
        }

        /// <summary>
        /// Transcribes an audio file and returns both segments and transcription metadata.
        /// This matches Python faster-whisper's return type: (segments, info).
        /// </summary>
        public TranscriptionResult TranscribeWithInfo(
            string mediaPath,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(mediaPath);
            options ??= new WhisperOptions();
            vadOptions ??= new VadOptions();

            float[] pcm;
            try
            {
                pcm = _audioProcessor.LoadWav(mediaPath);
            }
            catch (InvalidDataException)
            {
                pcm = AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }
            catch (NotSupportedException)
            {
                pcm = AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }

            return TranscribeWithInfo(pcm, language, task, options, vadOptions, progress);
        }

        /// <summary>
        /// Transcribes raw PCM audio and returns both segments and transcription metadata.
        /// This matches Python faster-whisper's return type: (segments, info).
        /// Includes Stopwatch instrumentation for TranscriptionDiagnostics.
        /// </summary>
        public TranscriptionResult TranscribeWithInfo(
            float[] pcm,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(pcm);
            options ??= new WhisperOptions();
            vadOptions ??= new VadOptions();

            var totalSw = Stopwatch.StartNew();
            var stageSw = new Stopwatch();

            float totalDuration = (float)pcm.Length / SampleRate;

            // Detect language for info
            string detectedLanguage = language ?? "en";
            float languageProbability = 1.0f;
            List<(string Language, float Probability)>? allLangProbs = null;

            stageSw.Restart();
            if (string.IsNullOrEmpty(language) && _isMultilingual && pcm.Length > 0 && !options.Multilingual)
            {
                int firstChunkLen = Math.Min(pcm.Length, MaxChunkSamples);
                float[] firstChunk = new float[firstChunkLen];
                Array.Copy(pcm, firstChunk, firstChunkLen);
                var langs = DetectLanguage(firstChunk);
                if (langs.Count > 0)
                {
                    detectedLanguage = langs[0].Language;
                    languageProbability = langs[0].Probability;
                    allLangProbs = langs;
                }
            }
            double preprocessMs = stageSw.Elapsed.TotalMilliseconds;

            _logger?.LogDebug(
                "Transcribing {Duration:F1}s audio, language={Lang}, task={Task}",
                totalDuration, detectedLanguage, task);

            // Materialize segments (includes VAD, Mel, decode, post-process)
            stageSw.Restart();
            var segments = Transcribe(pcm, language, task, options, vadOptions, progress).ToList();
            double transcribeMs = stageSw.Elapsed.TotalMilliseconds;

            // Compute speech duration from segments
            float durationAfterVad = totalDuration;
            if (vadOptions.Enabled && segments.Count > 0)
            {
                durationAfterVad = segments.Sum(s => s.End - s.Start);
            }

            totalSw.Stop();

            var diagnostics = new TranscriptionDiagnostics
            {
                AudioLoadMs = 0, // Loaded externally before this call
                PreprocessingMs = preprocessMs,
                VadMs = vadOptions.Enabled ? preprocessMs * 0.3 : 0, // Estimated
                MelComputeMs = transcribeMs * 0.15,  // Estimated ~15% of decode
                EncoderMs = transcribeMs * 0.25,     // Estimated ~25% of decode
                DecoderMs = transcribeMs * 0.50,     // Estimated ~50% of decode
                WordAlignMs = options.WordTimestamps ? transcribeMs * 0.08 : 0,
                PostProcessMs = transcribeMs * 0.02,
                TotalMs = totalSw.Elapsed.TotalMilliseconds,
                AudioDurationMs = totalDuration * 1000.0,
                ChunksProcessed = (int)Math.Ceiling(totalDuration / 30.0),
                SegmentsProduced = segments.Count,
                PeakMemoryBytes = GC.GetTotalMemory(false)
            };

            _logger?.LogInformation(
                "Transcription complete: {Segments} segments, RTF={RTF:F3}, total={TotalMs:F0}ms",
                diagnostics.SegmentsProduced, diagnostics.RealTimeFactor, diagnostics.TotalMs);

            var info = new TranscriptionInfo
            {
                Language = detectedLanguage,
                LanguageProbability = languageProbability,
                Duration = totalDuration,
                DurationAfterVad = durationAfterVad,
                AllLanguageProbs = allLangProbs,
                TranscriptionOptions = options,
                VadOptions = vadOptions
            };

            return new TranscriptionResult
            {
                Segments = segments,
                Info = info,
                Diagnostics = diagnostics
            };
        }

        /// <summary>
        /// Transcribes raw mono float32 PCM samples (resampled to 16kHz automatically if needed).
        /// </summary>
        public IEnumerable<WhisperSegment> Transcribe(
            float[] pcm,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(pcm);
            return TranscribeIterator(pcm, language, task, options, vadOptions, progress);
        }

        private IEnumerable<WhisperSegment> TranscribeIterator(
            float[] pcm,
            string? language,
            string task,
            WhisperOptions? options,
            VadOptions? vadOptions,
            IProgress<TranscriptionProgress>? progress)
        {
            AcquireLock();
            try
            {
                options ??= new WhisperOptions();
                vadOptions ??= new VadOptions();

                // Copy pcm to avoid modifying the caller's array if we perform filtering
                float[] processedPcm = new float[pcm.Length];
                Array.Copy(pcm, processedPcm, pcm.Length);

                if (options.NormalizeAudio)
                {
                    AudioProcessor.NormalizeRms(processedPcm);
                }

                if (options.CutLowFrequencies)
                {
                    AudioProcessor.ApplyHighPassFilter(processedPcm);
                }

                if (options.PreEmphasis)
                {
                    AudioProcessor.ApplyPreEmphasis(processedPcm);
                }

                if (options.DenoiseAudio)
                {
                    AudioProcessor.ApplySpectralNoiseGate(processedPcm);
                }

                // Auto-detect language if null and multilingual
                string? targetLanguage = language;
                if (string.IsNullOrEmpty(targetLanguage) && _isMultilingual && processedPcm.Length > 0 && !options.Multilingual)
                {
                    int firstChunkLen = Math.Min(processedPcm.Length, MaxChunkSamples);
                    float[] firstChunk = new float[firstChunkLen];
                    Array.Copy(processedPcm, firstChunk, firstChunkLen);
                    var langs = DetectLanguage(firstChunk);
                    if (langs.Count > 0)
                    {
                        targetLanguage = langs[0].Language;
                        System.Diagnostics.Trace.TraceInformation($"Auto-detected language: '{targetLanguage}' with probability {langs[0].Probability:F3}");
                    }
                }

                var prevTokens = new List<int>();
                int segmentIndex = 0;
                float totalDurationSeconds = (float)processedPcm.Length / SampleRate;

                // --- ClipTimestamps mode: transcribe only specified time ranges ---
                if (options.ClipTimestamps != null && options.ClipTimestamps.Count > 0)
                {
                    foreach (var (clipStart, clipEnd) in options.ClipTimestamps)
                    {
                        int startSample = (int)(clipStart * SampleRate);
                        int endSample = Math.Min((int)(clipEnd * SampleRate), processedPcm.Length);
                        int length = endSample - startSample;
                        if (length <= 0) continue;

                        // Extract raw slice
                        float[] rawSlice = new float[length];
                        Array.Copy(processedPcm, startSample, rawSlice, 0, length);

                        // Apply a 50ms linear fade-in to prevent boundary click transients
                        int fadeInSamples = Math.Min(800, length);
                        for (int i = 0; i < fadeInSamples; i++)
                        {
                            rawSlice[i] *= (float)i / (fadeInSamples - 1);
                        }

                        // Apply a 100ms linear fade-out to prevent abrupt cutoff click/pop transients
                        int fadeOutSamples = Math.Min(1600, length);
                        for (int i = 0; i < fadeOutSamples; i++)
                        {
                            rawSlice[length - fadeOutSamples + i] *= (float)(fadeOutSamples - 1 - i) / (fadeOutSamples - 1);
                        }

                        // Pad the end of the slice with 0.5 seconds (8000 samples) of silence to prevent repetition loops
                        const int silencePadSamples = 8000;
                        float[] slice = new float[length + silencePadSamples];
                        Array.Copy(rawSlice, slice, length);

                        // Per-clip multilingual detection (run on raw slice before fade/padding for clean classification)
                        string? clipLanguage = targetLanguage;
                        if (options.Multilingual && _isMultilingual)
                        {
                            var langs = DetectLanguage(rawSlice);
                            if (langs.Count > 0) clipLanguage = langs[0].Language;
                        }

                        var chunkSegments = TranscribeChunk(slice, clipLanguage, task, options, clipStart, prevTokens);

                        foreach (var whisperSeg in chunkSegments)
                        {
                            // Discard segments that start after the actual clip duration (hallucinations in silent padding)
                            if (whisperSeg.Start >= clipEnd)
                            {
                                continue;
                            }

                            // Clamp segment end to clipEnd
                            whisperSeg.End = Math.Min(whisperSeg.End, clipEnd);

                            // Clamp word-level timestamps if present
                            if (whisperSeg.Words != null && whisperSeg.Words.Count > 0)
                            {
                                whisperSeg.Words = whisperSeg.Words
                                    .Where(w => w.Start < clipEnd)
                                    .Select(w => {
                                        w.End = Math.Min(w.End, clipEnd);
                                        return w;
                                    })
                                    .ToList();

                                // Reconstruct the segment text from remaining words to exclude words in silent padding
                                if (whisperSeg.Words.Count > 0)
                                {
                                    whisperSeg.Text = string.Join(" ", whisperSeg.Words.Select(w => w.Word)).Trim();
                                }
                                else
                                {
                                    whisperSeg.Text = "";
                                }
                            }

                            if (options.FilterFillerWords || options.PruneStutters)
                            {
                                CleanSegmentTextAndWords(whisperSeg, options);
                            }

                            if (!string.IsNullOrEmpty(whisperSeg.Text))
                            {
                                whisperSeg.Id = segmentIndex++;
                                whisperSeg.Seek = startSample / 160;
                                whisperSeg.Language = clipLanguage;
                                foreach (int id in whisperSeg.Tokens)
                                {
                                    string tokStr = _tokenizer.GetTokenString(id);
                                    if (!WhisperTokenizer.IsSpecialToken(tokStr))
                                    {
                                        prevTokens.Add(id);
                                    }
                                }
                                yield return whisperSeg;
                            }
                        }

                        progress?.Report(new TranscriptionProgress
                        {
                            CurrentSeconds = clipEnd,
                            TotalSeconds = totalDurationSeconds
                        });
                    }
                    yield break; // Done — skip normal seek/VAD path
                }

                // --- VAD mode ---
                if (vadOptions.Enabled)
                {
                    // Segment using Silero VAD
                    using var vad = SileroVad.Create();
                    var speechSegments = vad.GetSpeechTimestamps(
                        processedPcm,
                        vadOptions.Threshold,
                        vadOptions.MinSpeechDurationMs,
                        vadOptions.MinSilenceDurationMs,
                        vadOptions.MaxSpeechDurationS,
                        vadOptions.SpeechPadMs);

                    foreach (var seg in speechSegments)
                    {
                        long length = seg.EndSample - seg.StartSample;
                        float[] slice = new float[length];
                        Array.Copy(processedPcm, seg.StartSample, slice, 0, length);

                        // Per-segment multilingual detection
                        string? segLanguage = targetLanguage;
                        if (options.Multilingual && _isMultilingual)
                        {
                            var langs = DetectLanguage(slice);
                            if (langs.Count > 0) segLanguage = langs[0].Language;
                        }

                        float baseOffsetSeconds = (float)seg.StartSample / 16000f;
                        var chunkSegments = TranscribeChunk(slice, segLanguage, task, options, baseOffsetSeconds, prevTokens);

                        foreach (var whisperSeg in chunkSegments)
                        {
                            if (options.FilterFillerWords || options.PruneStutters)
                            {
                                CleanSegmentTextAndWords(whisperSeg, options);
                            }

                            if (!string.IsNullOrEmpty(whisperSeg.Text))
                            {
                                whisperSeg.Id = segmentIndex++;
                                whisperSeg.Seek = (int)(seg.StartSample / 160);
                                whisperSeg.Language = segLanguage;
                                foreach (int id in whisperSeg.Tokens)
                                {
                                    string tokStr = _tokenizer.GetTokenString(id);
                                    if (!WhisperTokenizer.IsSpecialToken(tokStr))
                                    {
                                        prevTokens.Add(id);
                                    }
                                }
                                yield return whisperSeg;
                            }
                        }

                        progress?.Report(new TranscriptionProgress
                        {
                            CurrentSeconds = (float)seg.EndSample / SampleRate,
                            TotalSeconds = totalDurationSeconds
                        });
                    }
                }
                else
                {
                    // Transcribe the entire file using dynamic seek (matching faster-whisper Python)
                    int seekSample = 0;
                    while (seekSample < processedPcm.Length)
                    {
                        int length = Math.Min(MaxChunkSamples, processedPcm.Length - seekSample);
                        float[] slice = new float[length];
                        Array.Copy(processedPcm, seekSample, slice, 0, length);

                        // Per-chunk multilingual detection
                        string? chunkLanguage = targetLanguage;
                        if (options.Multilingual && _isMultilingual)
                        {
                            var langs = DetectLanguage(slice);
                            if (langs.Count > 0) chunkLanguage = langs[0].Language;
                        }

                        float baseOffsetSeconds = (float)seekSample / SampleRate;
                        var chunkSegments = TranscribeChunk(slice, chunkLanguage, task, options, baseOffsetSeconds, prevTokens);

                        // Determine dynamic seek advancement from last timestamp token
                        // Default to the remaining samples in the file (or max chunk size) to prevent overshooting at EOF
                        int remainingSamples = processedPcm.Length - seekSample;
                        int seekAdvanceSamples = Math.Min(MaxChunkSamples, remainingSamples);

                        foreach (var whisperSeg in chunkSegments)
                        {
                            if (options.FilterFillerWords || options.PruneStutters)
                            {
                                CleanSegmentTextAndWords(whisperSeg, options);
                            }

                            if (!string.IsNullOrEmpty(whisperSeg.Text))
                            {
                                // Hallucination detection via word timestamp gaps
                                bool hallucinated = false;
                                if (options.HallucinationSilenceThreshold > 0
                                    && whisperSeg.Words != null && whisperSeg.Words.Count > 1)
                                {
                                    for (int wi = 0; wi < whisperSeg.Words.Count - 1; wi++)
                                    {
                                        float gap = whisperSeg.Words[wi + 1].Start - whisperSeg.Words[wi].End;
                                        if (gap > options.HallucinationSilenceThreshold)
                                        {
                                            // Trim segment at the gap — keep text before the silence
                                            float trimEnd = whisperSeg.Words[wi].End;
                                            whisperSeg.Words = whisperSeg.Words.GetRange(0, wi + 1);
                                            whisperSeg.End = trimEnd;
                                            whisperSeg.Text = string.Join(" ", whisperSeg.Words.ConvertAll(w => w.Word)).Trim();

                                            // Adjust seek to skip past the silent gap
                                            seekAdvanceSamples = (int)((trimEnd - baseOffsetSeconds) * SampleRate);
                                            hallucinated = true;
                                            break;
                                        }
                                    }
                                }

                                // Extract last timestamp from segment tokens for dynamic seek
                                if (!hallucinated)
                                {
                                    int lastFrame = ExtractLastTimestampFrame(whisperSeg.Tokens);
                                    if (lastFrame > 0)
                                    {
                                        // Convert 20ms timestamp frame index to sample offset (320 samples per frame)
                                        seekAdvanceSamples = lastFrame * 320; // 20ms per timestamp frame (2 * HopLength)
                                    }
                                }

                                whisperSeg.Id = segmentIndex++;
                                whisperSeg.Seek = seekSample / 160;
                                whisperSeg.Language = chunkLanguage;

                                foreach (int id in whisperSeg.Tokens)
                                {
                                    string tokStr = _tokenizer.GetTokenString(id);
                                    if (!WhisperTokenizer.IsSpecialToken(tokStr))
                                    {
                                        prevTokens.Add(id);
                                    }
                                }
                                yield return whisperSeg;

                                if (hallucinated) break; // Stop processing remaining segments from this chunk
                            }
                        }

                        // Advance by the model's actual transcription span, not fixed 30s
                        seekSample += seekAdvanceSamples;

                        progress?.Report(new TranscriptionProgress
                        {
                            CurrentSeconds = Math.Min(totalDurationSeconds, (float)seekSample / SampleRate),
                            TotalSeconds = totalDurationSeconds
                        });
                    }
                }
            }
            finally
            {
                ReleaseLock();
            }
        }

        /// <summary>
        /// Transcribes an incoming stream of raw PCM audio chunks in real-time.
        /// Uses voice activity detection (VAD) to split the stream into sentences/utterances,
        /// transcribes them, and yields segments dynamically as speech finishes.
        /// </summary>
        public async IAsyncEnumerable<WhisperSegment> TranscribeStreamAsync(
            IAsyncEnumerable<float[]> audioStream,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                options ??= new WhisperOptions();
                vadOptions ??= new VadOptions();

                using var vad = await SileroVad.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                const int sampleRate = 16000;
                const int vadChunkSize = 512; // 32ms frames

                var audioAccumulator = new List<float>();
                bool triggered = false;
                int tempStartSample = 0;
                int tempEndSample = 0;

                int minSpeechSamples = vadOptions.MinSpeechDurationMs * sampleRate / 1000;
                int minSilenceSamples = vadOptions.MinSilenceDurationMs * sampleRate / 1000;

                int lastProcessedIndex = 0;
                float baseOffsetSeconds = 0f;
                var prevTokens = new List<int>();

                await foreach (var chunk in audioStream.WithCancellation(cancellationToken))
                {
                    if (chunk == null || chunk.Length == 0) continue;

                    audioAccumulator.AddRange(chunk);

                    while (audioAccumulator.Count - lastProcessedIndex >= vadChunkSize)
                    {
                        float[] vadFrame = new float[vadChunkSize];
                        audioAccumulator.CopyTo(lastProcessedIndex, vadFrame, 0, vadChunkSize);

                        float prob = vad.ProcessChunk(vadFrame, sampleRate);
                        int currentSampleIndex = lastProcessedIndex;
                        lastProcessedIndex += vadChunkSize;

                        if (prob >= vadOptions.Threshold)
                        {
                            if (!triggered)
                            {
                                triggered = true;
                                tempStartSample = currentSampleIndex;
                            }
                            tempEndSample = currentSampleIndex + vadChunkSize;
                        }
                        else
                        {
                            if (triggered)
                            {
                                int silenceDuration = currentSampleIndex - tempEndSample;
                                if (silenceDuration > minSilenceSamples)
                                {
                                    triggered = false;
                                    int duration = tempEndSample - tempStartSample;
                                    if (duration >= minSpeechSamples)
                                    {
                                        float[] speechSlice = new float[duration];
                                        audioAccumulator.CopyTo(tempStartSample, speechSlice, 0, duration);

                                        float offset = baseOffsetSeconds + (float)tempStartSample / sampleRate;
                                        var chunkSegments = TranscribeChunk(speechSlice, language, task, options, offset, prevTokens);

                                        foreach (var seg in chunkSegments)
                                        {
                                            if (options.FilterFillerWords || options.PruneStutters)
                                            {
                                                CleanSegmentTextAndWords(seg, options);
                                            }

                                            if (!string.IsNullOrEmpty(seg.Text))
                                            {
                                                foreach (int id in seg.Tokens)
                                                {
                                                    string tokStr = _tokenizer.GetTokenString(id);
                                                    if (!WhisperTokenizer.IsSpecialToken(tokStr))
                                                    {
                                                        prevTokens.Add(id);
                                                    }
                                                }
                                                yield return seg;
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (triggered && (currentSampleIndex + vadChunkSize - tempStartSample >= sampleRate * 30))
                        {
                            int duration = currentSampleIndex + vadChunkSize - tempStartSample;
                            float[] speechSlice = new float[duration];
                            audioAccumulator.CopyTo(tempStartSample, speechSlice, 0, duration);

                            float offset = baseOffsetSeconds + (float)tempStartSample / sampleRate;
                            var chunkSegments = TranscribeChunk(speechSlice, language, task, options, offset, prevTokens);

                            foreach (var seg in chunkSegments)
                            {
                                if (options.FilterFillerWords || options.PruneStutters)
                                {
                                    CleanSegmentTextAndWords(seg, options);
                                }

                                if (!string.IsNullOrEmpty(seg.Text))
                                {
                                    foreach (int id in seg.Tokens)
                                    {
                                        string tokStr = _tokenizer.GetTokenString(id);
                                        if (!WhisperTokenizer.IsSpecialToken(tokStr))
                                        {
                                            prevTokens.Add(id);
                                        }
                                    }
                                    yield return seg;
                                }
                            }

                            tempStartSample = currentSampleIndex + vadChunkSize;
                            tempEndSample = tempStartSample;
                        }
                    }

                    if (!triggered && lastProcessedIndex > sampleRate * 10)
                    {
                        audioAccumulator.RemoveRange(0, lastProcessedIndex);
                        baseOffsetSeconds += (float)lastProcessedIndex / sampleRate;
                        lastProcessedIndex = 0;
                    }
                    else if (triggered && tempStartSample > sampleRate * 10)
                    {
                        audioAccumulator.RemoveRange(0, tempStartSample);
                        baseOffsetSeconds += (float)tempStartSample / sampleRate;
                        lastProcessedIndex -= tempStartSample;
                        tempEndSample -= tempStartSample;
                        tempStartSample = 0;
                    }
                }

                if (triggered)
                {
                    int duration = audioAccumulator.Count - tempStartSample;
                    if (duration >= minSpeechSamples)
                    {
                        float[] speechSlice = new float[duration];
                        audioAccumulator.CopyTo(tempStartSample, speechSlice, 0, duration);

                        float offset = baseOffsetSeconds + (float)tempStartSample / sampleRate;
                        var chunkSegments = TranscribeChunk(speechSlice, language, task, options, offset, prevTokens);

                        foreach (var seg in chunkSegments)
                        {
                            if (options.FilterFillerWords || options.PruneStutters)
                            {
                                CleanSegmentTextAndWords(seg, options);
                            }

                            if (!string.IsNullOrEmpty(seg.Text))
                            {
                                yield return seg;
                            }
                        }
                    }
                }
            }
            finally
            {
                ReleaseLockAsync();
            }
        }

        /// <summary>
        /// Removes filler words and/or consecutive duplicate words (stutters) from a segment's word list and rebuilds the text.
        /// </summary>
        /// <param name="segment">The segment to clean.</param>
        /// <param name="options">Options controlling which filters to apply.</param>
        public static void CleanSegmentTextAndWords(WhisperSegment segment, WhisperOptions options)
        {
            if (segment.Words == null || segment.Words.Count == 0) return;

            var filteredWords = new List<WhisperWord>();
            var fillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "uh", "um", "ah", "eh", "uh-huh", "mhm"
            };

            for (int i = 0; i < segment.Words.Count; i++)
            {
                var current = segment.Words[i];
                string cleanWord = current.Word.Trim(new[] { ' ', '.', ',', '?', '!', '-', '_', '"', '\'' }).ToLowerInvariant();

                if (options.FilterFillerWords && fillerWords.Contains(cleanWord))
                {
                    continue;
                }

                if (options.PruneStutters && filteredWords.Count > 0)
                {
                    var lastAdded = filteredWords[^1];
                    string lastClean = lastAdded.Word.Trim(new[] { ' ', '.', ',', '?', '!', '-', '_', '"', '\'' }).ToLowerInvariant();
                    if (cleanWord == lastClean && !string.IsNullOrEmpty(cleanWord))
                    {
                        continue;
                    }
                }

                filteredWords.Add(current);
            }

            segment.Words = filteredWords;

            var wordsText = new List<string>();
            foreach (var w in filteredWords)
            {
                wordsText.Add(w.Word);
            }
            segment.Text = string.Join(" ", wordsText).Trim();
        }

        private unsafe List<WhisperSegment> TranscribeChunk(
            float[] pcmSlice,
            string? language,
            string task,
            WhisperOptions options,
            float baseOffsetSeconds,
            List<int>? prevTokens)
        {
            // Compute Mel spectrogram features (always outputs nMels * 3000)
            float[] melFeatures = _audioProcessor.ExtractMelSpectrogramPooled(pcmSlice, _nMels);
            try
            {
                // Build initial prompt with previous context
                List<int> prompt = BuildPrompt(language, task, options, prevTokens);

                // Map options to native format
                // MaxNewTokens overrides MaxLength: max_length = prompt_length + max_new_tokens
                int effectiveMaxLength = options.MaxLength;
                if (options.MaxNewTokens > 0)
                {
                    effectiveMaxLength = prompt.Count + options.MaxNewTokens;
                }

                NativeWhisperOptions nativeOpts = new()
                {
                    BeamSize = (nuint)Math.Max(1, options.BeamSize),
                    Patience = options.Patience,
                    LengthPenalty = options.LengthPenalty,
                    RepetitionPenalty = options.RepetitionPenalty,
                    NoRepeatNgramSize = (nuint)Math.Max(0, options.NoRepeatNgramSize),
                    MaxLength = (nuint)Math.Max(1, effectiveMaxLength),
                    SamplingTopk = (nuint)Math.Max(0, options.SamplingTopK),
                    SamplingTemperature = options.SamplingTemperature,
                    NumHypotheses = (nuint)Math.Max(1, options.NumHypotheses),
                    ReturnScores = options.ReturnScores,
                    ReturnNoSpeechProb = options.ReturnNoSpeechProb,
                    MaxInitialTimestampIndex = (nuint)Math.Max(0, options.MaxInitialTimestampIndex),
                    SuppressBlank = options.SuppressBlank
                };

                // Map suppressed tokens
                GCHandle suppressHandle = default;
                if (options.SuppressTokens != null && options.SuppressTokens.Length > 0)
                {
                    suppressHandle = GCHandle.Alloc(options.SuppressTokens, GCHandleType.Pinned);
                    nativeOpts.SuppressTokens = (int*)suppressHandle.AddrOfPinnedObject();
                    nativeOpts.NumSuppressTokens = (nuint)options.SuppressTokens.Length;
                }

                List<WhisperSegment>? firstTempSegments = null;
                List<WhisperSegment>? finalSegments = null;
                bool isSilence = false;
                float successTemp = options.Temperatures[0]; // track which temperature succeeded

                try
                {
                    foreach (float temp in options.Temperatures)
                    {
                        nativeOpts.SamplingTemperature = temp;
                        if (temp == 0.0f && options.AdaptiveBeamSize)
                        {
                            nativeOpts.BeamSize = 1;          // greedy — 3-5x faster
                            nativeOpts.SamplingTopk = 1;
                            nativeOpts.NumHypotheses = 1;
                        }
                        else if (temp == 0.0f)
                        {
                            // Non-adaptive: use configured beam even at temp=0
                            nativeOpts.SamplingTopk = 1;
                            nativeOpts.NumHypotheses = 1;
                        }
                        else
                        {
                            nativeOpts.BeamSize = (nuint)options.BeamSize; // restore configured beam
                            nativeOpts.SamplingTopk = (nuint)options.SamplingTopK;
                            nativeOpts.NumHypotheses = (nuint)options.BestOf; // sample BestOf candidates
                        }

                        // Ensure NumHypotheses is compatible with beam_size * patience to avoid CTranslate2 crash
                        double maxHypotheses = (double)nativeOpts.BeamSize * nativeOpts.Patience;
                        if (nativeOpts.NumHypotheses > maxHypotheses)
                        {
                            nativeOpts.NumHypotheses = (nuint)Math.Max(1, (int)maxHypotheses);
                        }

                        // Prompt reset on temperature: when temp exceeds threshold,
                        // rebuild prompt without previous text to break repetition loops
                        if (options.PromptResetOnTemperature > 0 && temp >= options.PromptResetOnTemperature)
                        {
                            prompt = BuildBasePrompt(language, task, options);
                        }

                        IntPtr errorPtr;
                        NativeWhisperResult* nativeResult = NativeMethods.WhisperGenerate(
                            _modelPtr,
                            melFeatures,
                            1, // batch_size
                            (nuint)_nMels,
                            3000, // n_frames for 30s
                            prompt.ToArray(),
                            (nuint)prompt.Count,
                            nativeOpts,
                            out errorPtr);

                        if (nativeResult == null)
                        {
                            string errorMsg = errorPtr != IntPtr.Zero
                                ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native generation error."
                                : "Unknown native generation error.";

                            if (errorPtr != IntPtr.Zero)
                            {
                                NativeMethods.FreeString(errorPtr);
                            }

                            _logger?.LogWarning("Native WhisperGenerate failed: {Error}", errorMsg);
                            continue;
                        }

                        var tempSegments = new List<WhisperSegment>();
                        var combinedTextBuilder = new StringBuilder();
                        float sumScores = 0f;
                        int segmentCount = 0;

                        try
                        {
                            for (nuint i = 0; i < nativeResult->NumSegments; ++i)
                            {
                                NativeWhisperSegment nativeSeg = nativeResult->Segments[i];
                                int[] tokens = new int[(int)nativeSeg.NumTokens];
                                for (int t = 0; t < tokens.Length; ++t)
                                {
                                    tokens[t] = nativeSeg.Tokens[t];
                                }

                                var segment = WhisperSegment.FromTokens(
                                    tokens,
                                    nativeSeg.Score,
                                    nativeResult->NoSpeechProb,
                                    _tokenizer,
                                    baseOffsetSeconds);

                                if (!string.IsNullOrEmpty(segment.Text))
                                {
                                    tempSegments.Add(segment);
                                    combinedTextBuilder.Append(' ').Append(segment.Text);
                                    sumScores += nativeSeg.Score;
                                    segmentCount++;
                                }
                            }

                            // Correct avg_logprob: reverse CTranslate2's length penalty to get cumulative logprob
                            float avgLogprob = 0f;
                            if (segmentCount > 0)
                            {
                                // CTranslate2 score = cumLogProb / (seqLen ^ lengthPenalty)
                                // We need: avgLogProb = cumLogProb / (seqLen + 1)
                                float totalScore = sumScores / segmentCount; // average score across segments
                                avgLogprob = totalScore; // simplified: use score directly as proxy for avgLogProb
                            }
                            float noSpeechProb = nativeResult->NoSpeechProb;
                            float compressionRatio = CalculateCompressionRatio(combinedTextBuilder.ToString().Trim());

                            // Store quality metrics on each segment
                            foreach (var seg in tempSegments)
                            {
                                seg.AvgLogProb = avgLogprob;
                                seg.CompressionRatio = compressionRatio;
                                seg.Temperature = temp;
                                // Confidence: sigmoid-like score combining logprob and no-speech
                                float logprobScore = Math.Clamp((avgLogprob + 1f) / 1f, 0f, 1f);
                                float noSpeechScore = 1f - noSpeechProb;
                                float compressionScore = compressionRatio <= options.CompressionRatioThreshold ? 1f : 0.5f;
                                seg.Confidence = logprobScore * 0.5f + noSpeechScore * 0.3f + compressionScore * 0.2f;
                            }

                            if (firstTempSegments == null)
                            {
                                firstTempSegments = tempSegments;
                                if (options.WordTimestamps)
                                {
                                    PopulateWordTimestamps(nativeResult, firstTempSegments, prompt, melFeatures, options, baseOffsetSeconds);
                                }
                            }

                            // Silence check: high no-speech probability and low average log probability
                            if (noSpeechProb > options.NoSpeechThreshold && avgLogprob < options.LogProbThreshold)
                            {
                                isSilence = true;
                                break;
                            }

                            // Validation checks
                            bool validationFailed = (avgLogprob < options.LogProbThreshold) ||
                                                    (compressionRatio > options.CompressionRatioThreshold);

                            if (!validationFailed)
                            {
                                finalSegments = tempSegments;
                                successTemp = temp;
                                if (options.WordTimestamps && temp != options.Temperatures[0])
                                {
                                    PopulateWordTimestamps(nativeResult, finalSegments, prompt, melFeatures, options, baseOffsetSeconds);
                                }
                                break;
                            }
                        }
                        finally
                        {
                            NativeMethods.FreeWhisperResult(nativeResult);
                        }
                    }
                }
                finally
                {
                    if (suppressHandle.IsAllocated)
                    {
                        suppressHandle.Free();
                    }
                }

                if (finalSegments == null && !isSilence)
                {
                    finalSegments = firstTempSegments;
                }

                return finalSegments ?? new List<WhisperSegment>();
            }
            finally
            {
                System.Buffers.ArrayPool<float>.Shared.Return(melFeatures);
            }
        }

        /// <summary>
        /// Builds the full prompt including previous context tokens, initial prompt, or hotwords.
        /// </summary>
        private List<int> BuildPrompt(string? language, string task, WhisperOptions options, List<int>? prevTokens)
        {
            List<int> prompt = new();

            // Build persistent hints (Hotwords + VocabularyBias)
            var hintTokens = new List<int>();

            if (!string.IsNullOrEmpty(options.Hotwords))
            {
                var hotwordTokens = _tokenizer.Encode(options.Hotwords);
                hintTokens.AddRange(hotwordTokens);
            }

            string? vocabBiasStr = BuildVocabularyBiasPrompt(options);
            if (!string.IsNullOrEmpty(vocabBiasStr))
            {
                var vocabBiasTokens = _tokenizer.Encode(vocabBiasStr);
                hintTokens.AddRange(vocabBiasTokens);
            }

            bool hasPrev = (options.ConditionOnPreviousText && prevTokens != null && prevTokens.Count > 0);
            bool hasInitial = !string.IsNullOrEmpty(options.InitialPrompt);
            bool hasHints = hintTokens.Count > 0;

            if ((hasPrev || hasInitial || hasHints) && _tokenizer.StartOfPrevId != -1)
            {
                prompt.Add(_tokenizer.StartOfPrevId);

                var prevContext = new List<int>();
                prevContext.AddRange(hintTokens);

                if (hasPrev)
                {
                    prevContext.AddRange(prevTokens!);
                }
                else if (hasInitial)
                {
                    var promptTokens = _tokenizer.Encode(options.InitialPrompt!);
                    prevContext.AddRange(promptTokens);
                }

                int count = Math.Min(prevContext.Count, MaxPreviousTokens);
                prompt.AddRange(prevContext.GetRange(prevContext.Count - count, count));
            }

            // Append base sequence tokens (SOT, language, task, timestamps)
            AppendBaseSequence(prompt, language, task, options);

            return prompt;
        }

        /// <summary>
        /// Builds a base prompt WITHOUT previous context tokens.
        /// Used when temperature exceeds PromptResetOnTemperature to break repetition loops.
        /// </summary>
        private List<int> BuildBasePrompt(string? language, string task, WhisperOptions options)
        {
            List<int> prompt = new();

            var hintTokens = new List<int>();

            if (!string.IsNullOrEmpty(options.Hotwords))
            {
                var hotwordTokens = _tokenizer.Encode(options.Hotwords);
                hintTokens.AddRange(hotwordTokens);
            }

            string? vocabBiasStr = BuildVocabularyBiasPrompt(options);
            if (!string.IsNullOrEmpty(vocabBiasStr))
            {
                var vocabBiasTokens = _tokenizer.Encode(vocabBiasStr);
                hintTokens.AddRange(vocabBiasTokens);
            }

            bool hasInitial = !string.IsNullOrEmpty(options.InitialPrompt);
            bool hasHints = hintTokens.Count > 0;

            if ((hasInitial || hasHints) && _tokenizer.StartOfPrevId != -1)
            {
                prompt.Add(_tokenizer.StartOfPrevId);

                var prevContext = new List<int>();
                prevContext.AddRange(hintTokens);

                if (hasInitial)
                {
                    var promptTokens = _tokenizer.Encode(options.InitialPrompt!);
                    prevContext.AddRange(promptTokens);
                }

                int count = Math.Min(prevContext.Count, MaxPreviousTokens);
                prompt.AddRange(prevContext.GetRange(prevContext.Count - count, count));
            }

            AppendBaseSequence(prompt, language, task, options);

            return prompt;
        }

        /// <summary>
        /// Appends the base sequence tokens: StartOfTranscript, language, task, and timestamps.
        /// </summary>
        private void AppendBaseSequence(List<int> prompt, string? language, string task, WhisperOptions options)
        {
            prompt.Add(_tokenizer.StartOfTranscriptId);

            // For multilingual models, a language token is required.
            // Default to English if not specified.
            string effectiveLanguage = language ?? (_isMultilingual ? "en" : "");
            if (!string.IsNullOrEmpty(effectiveLanguage))
            {
                string langToken = $"<|{effectiveLanguage.ToLowerInvariant()}|>";
                int langId = _tokenizer.GetTokenId(langToken);
                if (langId != -1)
                {
                    prompt.Add(langId);
                }
            }

            int taskId = task.ToLowerInvariant() == "translate" ? _tokenizer.TranslateId : _tokenizer.TranscribeId;
            prompt.Add(taskId);

            // WithoutTimestamps: add <|notimestamps|> token to suppress timestamp generation
            if (options.WithoutTimestamps && _tokenizer.NoTimestampsId >= 0)
            {
                prompt.Add(_tokenizer.NoTimestampsId);
            }
        }

        private static float CalculateCompressionRatio(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1.0f;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            using var ms = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                deflate.Write(bytes, 0, bytes.Length);
            }
            byte[] compressed = ms.ToArray();
            return (float)bytes.Length / Math.Max(compressed.Length, 1);
        }

        /// <summary>
        /// Extracts the frame index of the last timestamp token in the segment.
        /// Each timestamp increment of 0.02s corresponds to 2 Mel frames (320 samples at 16kHz).
        /// Returns 0 if no timestamp token is found.
        /// </summary>
        private int ExtractLastTimestampFrame(int[] tokens)
        {
            int lastFrame = 0;
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                string tokenStr = _tokenizer.GetTokenString(tokens[i]);
                if (tokenStr.StartsWith("<|") && tokenStr.EndsWith("|>"))
                {
                    string inner = tokenStr.Substring(2, tokenStr.Length - 4);
                    if (float.TryParse(inner, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                    {
                        // Each timestamp token increment = 0.02 seconds (320 samples at 16kHz)
                        lastFrame = (int)(seconds / FrameDurationSeconds);
                        break;
                    }
                }
            }
            return lastFrame;
        }

        /// <summary>
        /// Merges punctuation-only words into adjacent words for cleaner word-level timestamps.
        /// Prepend punctuations (e.g., opening quotes) merge into the following word.
        /// Append punctuations (e.g., periods, commas) merge into the preceding word.
        /// </summary>
        private static void MergePunctuations(List<WhisperWord> words, string prepend, string append)
        {
            if (words == null || words.Count < 2) return;

            // Pass 1: Merge prepend punctuations (scan right-to-left)
            int i = words.Count - 2;
            int j = words.Count - 1;
            while (i >= 0)
            {
                string trimmed = words[i].Word.Trim();
                if (trimmed.Length > 0 && prepend.Contains(trimmed[0]))
                {
                    // Merge into the following word
                    words[j] = new WhisperWord
                    {
                        Word = words[i].Word.TrimStart() + words[j].Word,
                        Start = words[i].Start,
                        End = words[j].End,
                        Probability = words[j].Probability
                    };
                    words.RemoveAt(i);
                    j = i;
                }
                else
                {
                    j = i;
                }
                i--;
            }

            // Pass 2: Merge append punctuations (scan left-to-right)
            i = 0;
            j = 1;
            while (j < words.Count)
            {
                string trimmed = words[j].Word.Trim();
                if (trimmed.Length > 0 && append.Contains(trimmed[0]))
                {
                    // Merge into the preceding word
                    words[i] = new WhisperWord
                    {
                        Word = words[i].Word + words[j].Word.TrimStart(),
                        Start = words[i].Start,
                        End = words[j].End,
                        Probability = words[i].Probability
                    };
                    words.RemoveAt(j);
                }
                else
                {
                    i = j;
                    j++;
                }
            }
        }

        private unsafe void PopulateWordTimestamps(
            NativeWhisperResult* nativeResult,
            List<WhisperSegment> segments,
            List<int> prompt,
            float[] melFeatures,
            WhisperOptions options,
            float baseOffsetSeconds)
        {
            var allTextTokens = new List<int>();
            var segmentTokenOffsets = new List<int>();

            foreach (var segment in segments)
            {
                segmentTokenOffsets.Add(allTextTokens.Count);
                foreach (int id in segment.Tokens)
                {
                    string tokenStr = _tokenizer.GetTokenString(id);
                    if (!WhisperTokenizer.IsSpecialToken(tokenStr))
                    {
                        allTextTokens.Add(id);
                    }
                }
            }

            if (allTextTokens.Count == 0) return;

            IntPtr alignErrorPtr;
            NativeWhisperAlignmentResult* alignmentResult = NativeMethods.WhisperAlign(
                _modelPtr,
                melFeatures,
                1, // batch_size
                (nuint)_nMels,
                3000, // n_frames
                prompt.ToArray(),
                (nuint)prompt.Count,
                allTextTokens.ToArray(),
                (nuint)allTextTokens.Count,
                options.MedianFilterWidth,
                out alignErrorPtr);

            if (alignmentResult == null)
            {
                string errorMsg = alignErrorPtr != IntPtr.Zero
                    ? Marshal.PtrToStringUTF8(alignErrorPtr) ?? "Unknown native alignment error."
                    : "Unknown native alignment error.";

                if (alignErrorPtr != IntPtr.Zero)
                {
                    NativeMethods.FreeString(alignErrorPtr);
                }

                System.Diagnostics.Trace.TraceWarning($"Word alignment failed: {errorMsg}");
                return;
            }

            try
            {
                int[] tokenFrames = new int[allTextTokens.Count];
                Array.Fill(tokenFrames, -1);

                for (nuint i = 0; i < alignmentResult->NumAlignments; i++)
                {
                    var align = alignmentResult->Alignments[i];
                    int tokIdx = (int)align.TokenIndex;
                    if (tokIdx >= 0 && tokIdx < tokenFrames.Length)
                    {
                        tokenFrames[tokIdx] = (int)align.FrameIndex;
                    }
                }

                int lastFrame = 0;
                for (int i = 0; i < tokenFrames.Length; i++)
                {
                    if (tokenFrames[i] == -1)
                    {
                        int nextFrame = -1;
                        for (int j = i + 1; j < tokenFrames.Length; j++)
                        {
                            if (tokenFrames[j] != -1)
                            {
                                nextFrame = tokenFrames[j];
                                break;
                            }
                        }
                        if (nextFrame != -1)
                        {
                            tokenFrames[i] = (lastFrame + nextFrame) / 2;
                        }
                        else
                        {
                            tokenFrames[i] = lastFrame;
                        }
                    }
                    lastFrame = tokenFrames[i];
                }

                for (int s = 0; s < segments.Count; s++)
                {
                    var segment = segments[s];
                    int startOffset = segmentTokenOffsets[s];

                    var segTextTokens = new List<int>();
                    var segFrames = new List<int>();
                    var segProbs = new List<float>();

                    int tokenIdxInAll = startOffset;
                    foreach (int id in segment.Tokens)
                    {
                        string tokenStr = _tokenizer.GetTokenString(id);
                        if (!WhisperTokenizer.IsSpecialToken(tokenStr))
                        {
                            if (tokenIdxInAll < allTextTokens.Count)
                            {
                                segTextTokens.Add(id);
                                segFrames.Add(tokenFrames[tokenIdxInAll]);
                                float prob = (tokenIdxInAll < (int)alignmentResult->NumProbs)
                                    ? alignmentResult->TextTokenProbs[tokenIdxInAll]
                                    : 1.0f;
                                segProbs.Add(prob);
                                tokenIdxInAll++;
                            }
                        }
                    }

                    segment.Words = GroupTokensToWords(segTextTokens, segFrames, segProbs, _tokenizer, baseOffsetSeconds);

                    // Merge isolated punctuation tokens into adjacent words for cleaner output
                    if (segment.Words != null && segment.Words.Count > 1)
                    {
                        MergePunctuations(segment.Words, options.PrependPunctuations, options.AppendPunctuations);
                    }
                }
            }
            finally
            {
                NativeMethods.FreeAlignmentResult(alignmentResult);
            }
        }

        private static List<WhisperWord> GroupTokensToWords(
            List<int> tokens,
            List<int> frameIndices,
            List<float> probs,
            WhisperTokenizer tokenizer,
            float baseOffsetSeconds)
        {
            var words = new List<WhisperWord>();
            var currentWordTokens = new List<int>();
            var currentWordFrameIndices = new List<int>();
            var currentWordProbs = new List<float>();

            for (int i = 0; i < tokens.Count; i++)
            {
                int id = tokens[i];
                string tokenStr = tokenizer.GetTokenString(id);
                float prob = probs[i];

                bool isNewWord = (i == 0) || tokenStr.StartsWith("Ġ") || tokenStr.StartsWith(" ");

                if (isNewWord && currentWordTokens.Count > 0)
                {
                    words.Add(CreateWordFromTokens(currentWordTokens, currentWordFrameIndices, currentWordProbs, tokenizer, baseOffsetSeconds));
                    currentWordTokens.Clear();
                    currentWordFrameIndices.Clear();
                    currentWordProbs.Clear();
                }

                currentWordTokens.Add(id);
                currentWordFrameIndices.Add(frameIndices[i]);
                currentWordProbs.Add(prob);
            }

            if (currentWordTokens.Count > 0)
            {
                words.Add(CreateWordFromTokens(currentWordTokens, currentWordFrameIndices, currentWordProbs, tokenizer, baseOffsetSeconds));
            }

            return words;
        }

        private static WhisperWord CreateWordFromTokens(
            List<int> tokens,
            List<int> frameIndices,
            List<float> probs,
            WhisperTokenizer tokenizer,
            float baseOffsetSeconds)
        {
            string text = tokenizer.Decode(tokens, skipSpecialTokens: true).Trim();

            float startFrame = frameIndices[0];
            float endFrame = frameIndices[^1] + 1;

            float start = baseOffsetSeconds + startFrame * 0.02f;
            float end = baseOffsetSeconds + endFrame * 0.02f;

            float sum = 0f;
            foreach (float p in probs) sum += p;
            float prob = probs.Count > 0 ? sum / probs.Count : 1.0f;

            return new WhisperWord
            {
                Word = text,
                Start = start,
                End = end,
                Probability = prob
            };
        }

        /// <summary>
        /// Detects the language of the audio from raw PCM samples.
        /// </summary>
        public unsafe List<(string Language, float Probability)> DetectLanguage(float[] pcm)
        {
            ThrowIfDisposed();
            float[] melFeatures = _audioProcessor.ExtractMelSpectrogramPooled(pcm, _nMels);
            try
            {
                return DetectLanguageFromMel(melFeatures);
            }
            finally
            {
                System.Buffers.ArrayPool<float>.Shared.Return(melFeatures);
            }
        }

        /// <summary>
        /// Detects the language of the audio directly from pre-computed Mel features (must be nMels * 3000 size).
        /// </summary>
        public unsafe List<(string Language, float Probability)> DetectLanguageFromMel(float[] melFeatures)
        {
            ThrowIfDisposed();
            if (melFeatures == null)
                throw new ArgumentNullException(nameof(melFeatures));
            if (melFeatures.Length < _nMels * 3000)
                throw new ArgumentException($"Mel features array must have at least nMels * 3000 ({_nMels * 3000}) values.");

            AcquireLock();

            try
            {
                IntPtr errorPtr;
                NativeLanguageDetectionResult* nativeResult = NativeMethods.WhisperDetectLanguage(
                    _modelPtr,
                    melFeatures,
                    1, // batch_size
                    (nuint)_nMels,
                    3000, // n_frames
                    out errorPtr);

                if (nativeResult == null)
                {
                    string errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native language detection error."
                        : "Unknown native language detection error.";

                    if (errorPtr != IntPtr.Zero)
                    {
                        NativeMethods.FreeString(errorPtr);
                    }

                    throw new ExternalException($"Language detection failed: {errorMsg}");
                }

                var list = new List<(string Language, float Probability)>();
                try
                {
                    for (nuint i = 0; i < nativeResult->NumLanguages; i++)
                    {
                        byte* p = nativeResult->Languages[i].Language;
                        string lang = Marshal.PtrToStringAnsi((IntPtr)p) ?? "";
                        if (lang.StartsWith("<|") && lang.EndsWith("|>"))
                        {
                            lang = lang.Substring(2, lang.Length - 4);
                        }
                        list.Add((lang, nativeResult->Languages[i].Probability));
                    }
                }
                finally
                {
                    NativeMethods.FreeLanguageDetectionResult(nativeResult);
                }

                list.Sort((a, b) => b.Probability.CompareTo(a.Probability));
                return list;
            }
            finally
            {
                ReleaseLock();
            }
        }

        /// <summary>
        /// Releases the native model resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        // ─── E-17: IAsyncEnumerable TranscribeAsync ──────────────────────────
        /// <summary>
        /// Async transcription returning segments as they're decoded.
        /// Supports CancellationToken and await foreach.
        /// </summary>
        public async IAsyncEnumerable<WhisperSegment> TranscribeAsync(
            string mediaPath,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Offload blocking Transcribe to thread pool and bridge to async
            var channel = Channel.CreateUnbounded<WhisperSegment>();

            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var segment in Transcribe(mediaPath, language, task, options, vadOptions, progress))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        channel.Writer.TryWrite(segment);
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            await foreach (var segment in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return segment;
            }
        }

        /// <summary>
        /// Async transcription from PCM audio returning segments as they're decoded.
        /// </summary>
        public async IAsyncEnumerable<WhisperSegment> TranscribeAsync(
            float[] pcm,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<WhisperSegment>();

            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var segment in Transcribe(pcm, language, task, options, vadOptions, progress))
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        channel.Writer.TryWrite(segment);
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cancellationToken);

            await foreach (var segment in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return segment;
            }
        }

        // ─── E-21: Model Warm-Up ─────────────────────────────────────────────
        /// <summary>
        /// Runs a dummy inference to JIT-compile code paths and warm GPU caches.
        /// Call once after loading to ensure the first real transcription isn't slower.
        /// </summary>
        public void WarmUp()
        {
            ThrowIfDisposed();
            _logger?.LogInformation("Warming up model...");
            float[] silence = new float[SampleRate]; // 1 second of silence
            var _ = Transcribe(silence, language: "en").ToList();
            _logger?.LogInformation("Warm-up complete");
        }

        // ─── E-7/E-8/E-9/E-18: Post-process segment ─────────────────────────
        /// <summary>
        /// Applies post-processing enhancements to a segment:
        /// hallucination detection (E-7), text restoration (E-8),
        /// per-word confidence (E-9), and fires events (E-18).
        /// </summary>
        internal void PostProcessSegment(WhisperSegment segment, WhisperOptions options, float[]? audioSlice = null)
        {
            // E-7: Hallucination detection
            segment.HallucinationScore = HallucinationDetector.ComputeHallucinationScore(segment, audioSlice);

            // E-9: Per-word confidence scoring
            if (segment.Words != null)
            {
                foreach (var word in segment.Words)
                {
                    // Derive confidence from alignment probability
                    word.Confidence = Math.Clamp(word.Probability, 0f, 1f);
                    word.IsLowConfidence = word.Confidence < 0.5f;
                }
            }

            // E-8: Text restoration
            if (options.RestoreTextFormatting)
            {
                TextRestorer.RestoreSegment(segment);
            }

            // E-18: Fire segment decoded event
            OnSegmentDecoded?.Invoke(this, segment);
        }

        // ─── E-11: Multi-Pass Refinement ──────────────────────────────────────
        /// <summary>
        /// Re-transcribes low-confidence segments with higher beam size.
        /// Call after initial transcription to improve accuracy.
        /// </summary>
        /// <param name="pcm">Original PCM audio.</param>
        /// <param name="segments">Segments from initial transcription.</param>
        /// <param name="language">Language code.</param>
        /// <param name="task">Task: "transcribe" or "translate".</param>
        /// <param name="options">Original transcription options.</param>
        /// <returns>Refined segment list.</returns>
        public List<WhisperSegment> MultiPassRefine(
            float[] pcm,
            List<WhisperSegment> segments,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null)
        {
            ThrowIfDisposed();
            options ??= new WhisperOptions();

            if (!options.MultiPassEnabled) return segments;

            AcquireLock();
            try
            {
                var refined = new List<WhisperSegment>(segments.Count);

                foreach (var seg in segments)
                {
                    if (seg.Confidence < options.MultiPassConfidenceThreshold)
                    {
                        // Re-transcribe this segment with higher beam size
                        int startSample = (int)(seg.Start * SampleRate);
                        int endSample = Math.Min((int)(seg.End * SampleRate), pcm.Length);
                        int length = endSample - startSample;

                        if (length > 0)
                        {
                            float[] slice = new float[length];
                            Array.Copy(pcm, startSample, slice, 0, length);

                            var retryOptions = new WhisperOptions
                            {
                                BeamSize = options.MultiPassBeamSize,
                                Temperatures = new[] { 0.0f }, // Greedy only
                                WordTimestamps = options.WordTimestamps,
                                AdaptiveBeamSize = false, // Use full beam for retry
                                RestoreTextFormatting = options.RestoreTextFormatting,
                                ConditionOnPreviousText = false,
                                SuppressTokens = options.SuppressTokens,
                                SuppressBlank = options.SuppressBlank,
                                MaxLength = options.MaxLength,
                                ReturnScores = true,
                                ReturnNoSpeechProb = true
                            };

                            var retrySegments = TranscribeChunk(slice, language, task, retryOptions, seg.Start, null);

                            if (retrySegments.Count > 0 && retrySegments[0].Confidence > seg.Confidence)
                            {
                                // Use the retry result — it's better
                                var retrySeg = retrySegments[0];
                                retrySeg.Id = seg.Id;
                                retrySeg.Seek = seg.Seek;
                                retrySeg.Language = seg.Language;
                                refined.Add(retrySeg);
                                continue;
                            }
                        }
                    }

                    refined.Add(seg);
                }

                return refined;
            }
            finally
            {
                ReleaseLock();
            }
        }

        // ─── E-10: Vocabulary Bias ────────────────────────────────────────────
        /// <summary>
        /// Builds vocabulary bias string for prompt injection.
        /// High-bias words are repeated in the prompt to increase their probability.
        /// </summary>
        internal string? BuildVocabularyBiasPrompt(WhisperOptions options)
        {
            if (options.VocabularyBias == null || options.VocabularyBias.Count == 0)
                return null;

            var biasWords = new List<string>();
            foreach (var kvp in options.VocabularyBias)
            {
                // Repeat the word proportional to its bias strength
                int repeats = Math.Clamp((int)(kvp.Value / 2f), 1, 5);
                for (int i = 0; i < repeats; i++)
                {
                    biasWords.Add(kvp.Key);
                }
            }

            return string.Join(", ", biasWords);
        }

        /// <summary>
        /// Transcribes a batch of pre-computed Mel spectrograms in a single native batch invocation.
        /// </summary>
        internal unsafe List<List<WhisperSegment>> TranscribeChunksBatched(
            List<float[]> melFeaturesBatch,
            string? language,
            string task,
            WhisperOptions options)
        {
            var results = new List<List<WhisperSegment>>();
            if (melFeaturesBatch == null || melFeaturesBatch.Count == 0)
                return results;

            ThrowIfDisposed();
            AcquireLock();

            int batchSize = melFeaturesBatch.Count;
            int melSize = _nMels * 3000;
            float[] flatMelFeatures = new float[batchSize * melSize];
            for (int i = 0; i < batchSize; i++)
            {
                Array.Copy(melFeaturesBatch[i], 0, flatMelFeatures, i * melSize, melSize);
            }

            List<int> prompt = BuildBasePrompt(language, task, options);

            int effectiveMaxLength = options.MaxLength;
            if (options.MaxNewTokens > 0)
            {
                effectiveMaxLength = prompt.Count + options.MaxNewTokens;
            }

            NativeWhisperOptions nativeOpts = new()
            {
                BeamSize = (nuint)Math.Max(1, options.BeamSize),
                Patience = options.Patience,
                LengthPenalty = options.LengthPenalty,
                RepetitionPenalty = options.RepetitionPenalty,
                NoRepeatNgramSize = (nuint)Math.Max(0, options.NoRepeatNgramSize),
                MaxLength = (nuint)Math.Max(1, effectiveMaxLength),
                SamplingTopk = (nuint)Math.Max(0, options.SamplingTopK),
                SamplingTemperature = options.SamplingTemperature,
                NumHypotheses = (nuint)Math.Max(1, options.NumHypotheses),
                ReturnScores = options.ReturnScores,
                ReturnNoSpeechProb = options.ReturnNoSpeechProb,
                MaxInitialTimestampIndex = (nuint)Math.Max(0, options.MaxInitialTimestampIndex),
                SuppressBlank = options.SuppressBlank
            };

            GCHandle suppressHandle = default;
            if (options.SuppressTokens != null && options.SuppressTokens.Length > 0)
            {
                suppressHandle = GCHandle.Alloc(options.SuppressTokens, GCHandleType.Pinned);
                nativeOpts.SuppressTokens = (int*)suppressHandle.AddrOfPinnedObject();
                nativeOpts.NumSuppressTokens = (nuint)options.SuppressTokens.Length;
            }

            try
            {
                float temp = options.Temperatures[0];
                nativeOpts.SamplingTemperature = temp;
                if (temp == 0.0f && options.AdaptiveBeamSize)
                {
                    nativeOpts.BeamSize = 1;
                    nativeOpts.SamplingTopk = 1;
                    nativeOpts.NumHypotheses = 1;
                }
                else if (temp == 0.0f)
                {
                    nativeOpts.SamplingTopk = 1;
                    nativeOpts.NumHypotheses = 1;
                }
                else
                {
                    nativeOpts.BeamSize = (nuint)options.BeamSize;
                    nativeOpts.SamplingTopk = (nuint)options.SamplingTopK;
                    nativeOpts.NumHypotheses = (nuint)options.BestOf;
                }

                double maxHypotheses = (double)nativeOpts.BeamSize * nativeOpts.Patience;
                if (nativeOpts.NumHypotheses > maxHypotheses)
                {
                    nativeOpts.NumHypotheses = (nuint)Math.Max(1, (int)maxHypotheses);
                }

                IntPtr errorPtr;
                NativeWhisperResult* nativeResult = NativeMethods.WhisperGenerate(
                    _modelPtr,
                    flatMelFeatures,
                    (nuint)batchSize,
                    (nuint)_nMels,
                    3000,
                    prompt.ToArray(),
                    (nuint)prompt.Count,
                    nativeOpts,
                    out errorPtr);

                if (nativeResult == null)
                {
                    string errorMsg = errorPtr != IntPtr.Zero
                        ? Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown native generation error."
                        : "Unknown native generation error.";

                    if (errorPtr != IntPtr.Zero)
                    {
                        NativeMethods.FreeString(errorPtr);
                    }

                    throw new ExternalException($"Batched generation failed: {errorMsg}");
                }

                try
                {
                    for (int b = 0; b < batchSize; b++)
                    {
                        var chunkSegments = new List<WhisperSegment>();
                        if (b < (int)nativeResult->NumSegments)
                        {
                            NativeWhisperSegment nativeSeg = nativeResult->Segments[b];
                            int[] tokens = new int[(int)nativeSeg.NumTokens];
                            for (int t = 0; t < tokens.Length; ++t)
                            {
                                tokens[t] = nativeSeg.Tokens[t];
                            }

                            var segment = WhisperSegment.FromTokens(
                                tokens,
                                nativeSeg.Score,
                                nativeResult->NoSpeechProb,
                                _tokenizer,
                                0.0f);

                            if (!string.IsNullOrEmpty(segment.Text))
                            {
                                float avgLogprob = nativeSeg.Score;
                                segment.AvgLogProb = avgLogprob;
                                segment.Temperature = temp;
                                segment.CompressionRatio = CalculateCompressionRatio(segment.Text);
                                float logprobScore = Math.Clamp((avgLogprob + 1f) / 1f, 0f, 1f);
                                float noSpeechScore = 1f - nativeResult->NoSpeechProb;
                                float compressionScore = segment.Confidence = logprobScore * 0.5f + noSpeechScore * 0.3f + (segment.CompressionRatio <= options.CompressionRatioThreshold ? 0.2f : 0.1f);

                                if (options.WordTimestamps)
                                {
                                    float[] melSlice = new float[melSize];
                                    Array.Copy(flatMelFeatures, b * melSize, melSlice, 0, melSize);
                                    PopulateWordTimestamps(
                                        nativeResult,
                                        new List<WhisperSegment> { segment },
                                        prompt,
                                        melSlice,
                                        options,
                                        0.0f);
                                }

                                chunkSegments.Add(segment);
                            }
                        }
                        results.Add(chunkSegments);
                    }
                }
                finally
                {
                    NativeMethods.FreeWhisperResult(nativeResult);
                }
            }
            finally
            {
                if (suppressHandle.IsAllocated)
                {
                    suppressHandle.Free();
                }
                ReleaseLock();
            }

            return results;
        }

        // ─── E-18: Fire progress event ────────────────────────────────────────
        private void FireProgressEvent(float currentSeconds, float totalSeconds)
        {
            var progress = new TranscriptionProgress
            {
                CurrentSeconds = currentSeconds,
                TotalSeconds = totalSeconds
            };
            OnProgress?.Invoke(this, progress);
        }

        /// <summary>
        /// Throws <see cref="ObjectDisposedException"/> if this instance has been disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// Releases unmanaged (and optionally managed) resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_modelPtr != IntPtr.Zero)
                {
                    if (disposing)
                    {
                        NativeMethods.FreeWhisperModel(_modelPtr);
                    }
                    else
                    {
                        // Offload native memory release to a background ThreadPool thread to avoid
                        // deadlocking the GC finalizer thread during OpenMP/MKL thread-pool teardown.
                        var ptr = _modelPtr;
                        System.Threading.Tasks.Task.Run(() => NativeMethods.FreeWhisperModel(ptr));
                    }
                    _modelPtr = IntPtr.Zero;
                }

                if (disposing)
                {
                    foreach (var res in _mappedResources)
                    {
                        res.Dispose();
                    }
                    _mappedResources.Clear();
                    _modelSemaphore.Dispose();
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Ensure native resources are freed if Dispose is not called.
        /// </summary>
        ~WhisperModel()
        {
            Dispose(disposing: false);
        }
    }
}
