// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Batched inference pipeline that processes multiple audio chunks simultaneously
    /// for higher throughput. This matches Python faster-whisper's BatchedInferencePipeline.
    /// </summary>
    /// <remarks>
    /// <para>Batched inference works by:
    /// 1. Splitting audio into chunks via VAD or fixed windows
    /// 2. Computing Mel spectrograms for all chunks in parallel
    /// 3. Sending batched Mel tensors to the native CTranslate2 engine
    /// 4. Post-processing results individually
    /// </para>
    /// <para>Typical speedup is 2-4x for long audio files on GPU, 1.5-2x on CPU.</para>
    /// <para>This class is <b>NOT</b> thread-safe.</para>
    /// </remarks>
    public class BatchedInferencePipeline : IDisposable
    {
        private readonly WhisperModel _model;
        private readonly int _batchSize;
        private bool _disposed;

        /// <summary>
        /// Creates a new BatchedInferencePipeline wrapping an existing WhisperModel.
        /// </summary>
        /// <param name="model">The loaded WhisperModel to use for inference.</param>
        /// <param name="batchSize">Maximum number of chunks to process simultaneously. Default is 8.</param>
        public BatchedInferencePipeline(WhisperModel model, int batchSize = 8)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be >= 1.");

            _model = model;
            _batchSize = batchSize;
        }

        /// <summary>
        /// Transcribes an audio file using batched inference for higher throughput.
        /// </summary>
        /// <param name="mediaPath">Path to the audio file (WAV, MP3, MP4, etc.).</param>
        /// <param name="language">Language code (e.g., "en"). Null for auto-detection.</param>
        /// <param name="task">Task: "transcribe" or "translate".</param>
        /// <param name="options">Transcription options.</param>
        /// <param name="vadOptions">VAD options. VAD is recommended for batched inference.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>A TranscriptionResult containing segments and metadata.</returns>
        public TranscriptionResult Transcribe(
            string mediaPath,
            string? language = null,
            string task = "transcribe",
            WhisperOptions? options = null,
            VadOptions? vadOptions = null,
            IProgress<TranscriptionProgress>? progress = null)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(mediaPath);

            float[] pcm = LoadAudio(mediaPath);
            return Transcribe(pcm, language, task, options, vadOptions, progress);
        }

        /// <summary>
        /// Transcribes raw PCM audio using batched inference for higher throughput.
        /// </summary>
        /// <param name="pcm">Raw mono float32 PCM samples at 16kHz.</param>
        /// <param name="language">Language code (e.g., "en"). Null for auto-detection.</param>
        /// <param name="task">Task: "transcribe" or "translate".</param>
        /// <param name="options">Transcription options.</param>
        /// <param name="vadOptions">VAD options. VAD is strongly recommended for batched inference.</param>
        /// <param name="progress">Optional progress reporter.</param>
        /// <returns>A TranscriptionResult containing segments and metadata.</returns>
        public TranscriptionResult Transcribe(
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
            vadOptions ??= new VadOptions { Enabled = true }; // VAD strongly recommended for batching

            float totalDuration = (float)pcm.Length / WhisperModel.SampleRate;

            // Step 1: Preprocess audio
            float[] processedPcm = PreprocessAudio(pcm, options);

            // Step 2: Split into chunks (via VAD or fixed windows)
            var chunks = SplitIntoChunks(processedPcm, vadOptions);

            // Step 3: Detect language from first chunk
            string detectedLanguage = language ?? "en";
            float languageProbability = 1.0f;
            List<(string Language, float Probability)>? allLangProbs = null;

            if (string.IsNullOrEmpty(language) && _model.IsMultilingual && processedPcm.Length > 0)
            {
                int firstChunkLen = Math.Min(processedPcm.Length, WhisperModel.MaxChunkSamples);
                float[] firstChunk = new float[firstChunkLen];
                Array.Copy(processedPcm, firstChunk, firstChunkLen);
                var langs = _model.DetectLanguage(firstChunk);
                if (langs.Count > 0)
                {
                    detectedLanguage = langs[0].Language;
                    languageProbability = langs[0].Probability;
                    allLangProbs = langs;
                }
            }

            // Step 4: Process chunks in batches
            var allSegments = new List<WhisperSegment>();
            int segmentIndex = 0;

            for (int batchStart = 0; batchStart < chunks.Count; batchStart += _batchSize)
            {
                int batchEnd = Math.Min(batchStart + _batchSize, chunks.Count);
                var batch = chunks.GetRange(batchStart, batchEnd - batchStart);

                // Compute Mel spectrograms in parallel
                var melBatch = new float[batch.Count][];
                try
                {
                    Parallel.For(0, batch.Count, i =>
                    {
                        melBatch[i] = _model.AudioProcessorInstance.ExtractMelSpectrogramPooled(
                            batch[i].Samples, _model.MelBins);
                    });

                    // Process each chunk through the model in a single native batch call
                    var batchMelList = melBatch.ToList();
                    var batchSegments = _model.TranscribeChunksBatched(batchMelList, detectedLanguage, task, options);

                    for (int i = 0; i < batch.Count; i++)
                    {
                        var chunk = batch[i];
                        var chunkSegments = batchSegments[i];

                        foreach (var seg in chunkSegments)
                        {
                            // Adjust timestamps to global offset
                            float offset = chunk.OffsetSeconds;
                            seg.Start += offset;
                            seg.End += offset;
                            seg.Id = segmentIndex++;
                            seg.Seek = (int)(chunk.StartSample / 160);
                            seg.Language = detectedLanguage;

                            if (seg.Words != null)
                            {
                                foreach (var word in seg.Words)
                                {
                                    word.Start += offset;
                                    word.End += offset;
                                }
                            }

                            if (options.FilterFillerWords || options.PruneStutters)
                            {
                                WhisperModel.CleanSegmentTextAndWords(seg, options);
                            }

                            allSegments.Add(seg);
                        }
                    }
                }
                finally
                {
                    for (int i = 0; i < batch.Count; i++)
                    {
                        if (melBatch[i] != null)
                        {
                            System.Buffers.ArrayPool<float>.Shared.Return(melBatch[i]);
                        }
                    }
                }

                // Report progress
                if (progress != null && chunks.Count > 0)
                {
                    float lastChunkEnd = batch.Last().OffsetSeconds +
                        (float)batch.Last().Samples.Length / WhisperModel.SampleRate;
                    progress.Report(new TranscriptionProgress
                    {
                        CurrentSeconds = lastChunkEnd,
                        TotalSeconds = totalDuration
                    });
                }
            }

            // Step 5: Sort by start time (batching may produce out-of-order for overlapping chunks)
            allSegments.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Re-index after sort
            for (int i = 0; i < allSegments.Count; i++)
            {
                allSegments[i].Id = i;
            }

            // Compute speech duration
            float durationAfterVad = totalDuration;
            if (vadOptions.Enabled && allSegments.Count > 0)
            {
                durationAfterVad = allSegments.Sum(s => s.End - s.Start);
            }

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
                Segments = allSegments,
                Info = info
            };
        }

        /// <summary>
        /// Batch size used for inference.
        /// </summary>
        public int BatchSize => _batchSize;

        private float[] PreprocessAudio(float[] pcm, WhisperOptions options)
        {
            float[] processedPcm = new float[pcm.Length];
            Array.Copy(pcm, processedPcm, pcm.Length);

            if (options.NormalizeAudio)
                AudioProcessor.NormalizeRms(processedPcm);
            if (options.CutLowFrequencies)
                AudioProcessor.ApplyHighPassFilter(processedPcm);
            if (options.PreEmphasis)
                AudioProcessor.ApplyPreEmphasis(processedPcm);
            if (options.DenoiseAudio)
                AudioProcessor.ApplySpectralNoiseGate(processedPcm);

            return processedPcm;
        }

        private List<AudioChunk> SplitIntoChunks(float[] processedPcm, VadOptions vadOptions)
        {
            var chunks = new List<AudioChunk>();

            if (vadOptions.Enabled)
            {
                // Use VAD to find speech segments
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

                    chunks.Add(new AudioChunk
                    {
                        Samples = slice,
                        StartSample = seg.StartSample,
                        OffsetSeconds = (float)seg.StartSample / WhisperModel.SampleRate
                    });
                }
            }
            else
            {
                // Fixed 30-second windows
                int seekSample = 0;
                while (seekSample < processedPcm.Length)
                {
                    int length = Math.Min(WhisperModel.MaxChunkSamples, processedPcm.Length - seekSample);
                    float[] slice = new float[length];
                    Array.Copy(processedPcm, seekSample, slice, 0, length);

                    chunks.Add(new AudioChunk
                    {
                        Samples = slice,
                        StartSample = seekSample,
                        OffsetSeconds = (float)seekSample / WhisperModel.SampleRate
                    });

                    seekSample += WhisperModel.MaxChunkSamples;
                }
            }

            return chunks;
        }

        private float[] LoadAudio(string mediaPath)
        {
            try
            {
                return _model.AudioProcessorInstance.LoadWav(mediaPath);
            }
            catch (InvalidDataException)
            {
                return AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }
            catch (NotSupportedException)
            {
                return AudioProcessor.DecodeAndResampleWithFFmpeg(mediaPath);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BatchedInferencePipeline));
        }

        /// <summary>
        /// Disposes the pipeline. Does NOT dispose the underlying WhisperModel.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private class AudioChunk
        {
            public float[] Samples { get; init; } = [];
            public long StartSample { get; init; }
            public float OffsetSeconds { get; init; }
        }
    }
}
