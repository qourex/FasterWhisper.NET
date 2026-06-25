// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Speech segment representing start and end in samples.
    /// </summary>
    public record SpeechSegment(long StartSample, long EndSample);

    /// <summary>
    /// Voice Activity Detection (VAD) wrapper using the Snakers4 Silero VAD ONNX model.
    /// </summary>
    public class SileroVad : IDisposable
    {
        private const string SileroUrl = "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx";
        private const string ExpectedSileroHash = "1A153A22F4509E292A94E67D6F9B85E8DEB25B4988682B7E174C65279D8788E3";
        private static readonly HttpClient s_httpClient = new();
        private readonly InferenceSession _session;
        private readonly string _stateInputName;
        private readonly string _stateOutputName;
        private readonly int[] _stateShape;
        private readonly float[] _stateBuffer;
        private bool _disposed;

        // Context window: Silero VAD v5 requires prepending context_size samples
        // (64 for 16kHz, 32 for 8kHz) to each chunk before inference.
        private float[]? _context;
        private long _lastSr;

        // Cached fields for zero-allocation ONNX inference runs
        private float[]? _inputBuffer;
        private DenseTensor<float>? _audioTensor;
        private DenseTensor<long>? _srTensor;
        private DenseTensor<float>? _stateTensor;
        private NamedOnnxValue[]? _onnxInputs;

        /// <summary>
        /// Creates a new Silero VAD instance, downloading the model if needed.
        /// </summary>
        /// <param name="modelPath">Path to the silero_vad.onnx file. If null, downloads to ~/.cache/qourex-fasterwhisper/silero_vad.onnx.</param>
        /// <param name="cancellationToken">Token to cancel the download operation.</param>
        public static async Task<SileroVad> CreateAsync(string? modelPath = null, CancellationToken cancellationToken = default)
        {
            bool isDefaultPath = string.IsNullOrEmpty(modelPath);
            if (isDefaultPath)
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache",
                    "qourex-fasterwhisper"
                );
                Directory.CreateDirectory(cacheDir);
                modelPath = Path.Combine(cacheDir, "silero_vad.onnx");
            }

            string finalPath = modelPath!;

            if (!File.Exists(finalPath))
            {
                var bytes = await s_httpClient.GetByteArrayAsync(SileroUrl, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(finalPath, bytes, cancellationToken).ConfigureAwait(false);
            }

            if (isDefaultPath)
            {
                VerifyDefaultModelHash(finalPath);
            }

            return new SileroVad(finalPath);
        }

        /// <summary>
        /// Synchronously creates a new instance of the <see cref="SileroVad"/> class.
        /// Uses the local cached model if present, otherwise blocks on the download.
        /// </summary>
        /// <param name="modelPath">Optional custom path to the ONNX model.</param>
        /// <returns>A new SileroVad instance.</returns>
        public static SileroVad Create(string? modelPath = null)
        {
            bool isDefaultPath = string.IsNullOrEmpty(modelPath);
            if (isDefaultPath)
            {
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cache",
                    "qourex-fasterwhisper"
                );
                modelPath = Path.Combine(cacheDir, "silero_vad.onnx");
            }

            string finalPath = modelPath!;

            if (!File.Exists(finalPath))
            {
                // Blocks synchronously for download on first run
                return Task.Run(() => CreateAsync(finalPath)).GetAwaiter().GetResult();
            }

            if (isDefaultPath)
            {
                VerifyDefaultModelHash(finalPath);
            }

            return new SileroVad(finalPath);
        }

        private static void VerifyDefaultModelHash(string modelPath)
        {
            if (!File.Exists(modelPath)) return;

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(modelPath);
            byte[] hashBytes = sha256.ComputeHash(stream);
            var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("X2"));
            }
            string actualHash = sb.ToString();

            if (!string.Equals(actualHash, ExpectedSileroHash, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(modelPath); } catch { /* Ignore file delete errors */ }
                throw new System.Security.Cryptography.CryptographicException(
                    $"Integrity check failed for Silero VAD model '{modelPath}'. Expected SHA256: {ExpectedSileroHash}, Actual: {actualHash}. File has been deleted."
                );
            }
        }

        private SileroVad(string modelPath)
        {
            _session = new InferenceSession(modelPath);

            // Silero VAD v5 expects inputs:
            // "input" -> [1, context_size + chunk_size] (e.g. [1, 576] for 16kHz)
            // "sr" -> scalar int64
            // "state" -> [2, 1, 128]
            // Outputs:
            // "output" -> [1, 1]
            // "stateN" -> [2, 1, 128] (next state)
            
            // Get state dimensions dynamically
            var stateInput = _session.InputMetadata.FirstOrDefault(x => x.Key.Contains("state")).Value;
            _stateInputName = stateInput != null ? _session.InputMetadata.First(x => x.Key.Contains("state")).Key : "state";
            _stateOutputName = _session.OutputMetadata.Keys.FirstOrDefault(x => x.Contains("state") || x.Contains("output_n")) ?? "stateN";

            var dims = stateInput?.Dimensions ?? new int[] { 2, 1, 128 };
            _stateShape = new int[dims.Length];
            int size = 1;
            for (int i = 0; i < dims.Length; i++)
            {
                int d = dims[i];
                if (d <= 0)
                {
                    if (i == 0) d = 2;
                    else if (i == 1) d = 1;
                    else d = 128;
                }
                _stateShape[i] = d;
                size *= d;
            }
            _stateBuffer = new float[size];
        }

        /// <summary>
        /// Resets the internal LSTM states and context of the VAD model. Call before starting a new audio stream.
        /// </summary>
        public void ResetState()
        {
            Array.Clear(_stateBuffer, 0, _stateBuffer.Length);
            _context = null;
            _lastSr = 0;
            _inputBuffer = null;
            _audioTensor = null;
            _srTensor = null;
            _stateTensor = null;
            _onnxInputs = null;
        }

        /// <summary>
        /// Process a single chunk of audio to get speech probability.
        /// Silero VAD v5 requires exactly 512 samples for 16kHz (or 256 for 8kHz).
        /// The context window is managed internally and prepended automatically.
        /// </summary>
        /// <param name="audioChunk">Audio chunk (must be exactly 512 samples for 16kHz, or 256 for 8kHz).</param>
        /// <param name="sampleRate">Sample rate (8000 or 16000).</param>
        /// <returns>Speech probability between 0.0 and 1.0.</returns>
        public float ProcessChunk(float[] audioChunk, long sampleRate = 16000)
        {
            int contextSize = sampleRate == 16000 ? 64 : 32;

            // Reset states if sample rate or batch size changed
            if (_lastSr != 0 && _lastSr != sampleRate)
            {
                ResetState();
            }

            // Initialize context with zeros if not set
            if (_context == null)
            {
                _context = new float[contextSize];
            }

            int requiredInputLength = contextSize + audioChunk.Length;
            if (_inputBuffer == null || _inputBuffer.Length != requiredInputLength || _lastSr != sampleRate)
            {
                _inputBuffer = new float[requiredInputLength];
                _audioTensor = new DenseTensor<float>(_inputBuffer, new int[] { 1, requiredInputLength });
                _srTensor = new DenseTensor<long>(new long[] { sampleRate }, new int[0]);
                _stateTensor = new DenseTensor<float>(_stateBuffer, _stateShape);
                
                _onnxInputs = new NamedOnnxValue[]
                {
                    NamedOnnxValue.CreateFromTensor("input", _audioTensor),
                    NamedOnnxValue.CreateFromTensor("sr", _srTensor),
                    NamedOnnxValue.CreateFromTensor(_stateInputName, _stateTensor)
                };
            }

            // Prepend context to the audio chunk: [context | audioChunk] into _inputBuffer
            Array.Copy(_context, 0, _inputBuffer, 0, contextSize);
            Array.Copy(audioChunk, 0, _inputBuffer, contextSize, audioChunk.Length);

            using var results = _session.Run(_onnxInputs);

            // Extract output probability
            var outputVal = results.FirstOrDefault(x => x.Name == "output")?.AsTensor<float>();
            float probability = outputVal?.FirstOrDefault() ?? 0f;

            // Extract next state and update state buffer
            var nextStateVal = results.FirstOrDefault(x => x.Name == _stateOutputName)?.AsTensor<float>();
            if (nextStateVal != null)
            {
                int idx = 0;
                foreach (var val in nextStateVal)
                {
                    _stateBuffer[idx++] = val;
                }
            }

            // Update context: take the last context_size samples from the full input
            Array.Copy(_inputBuffer, _inputBuffer.Length - contextSize, _context, 0, contextSize);

            _lastSr = sampleRate;

            return probability;
        }

        /// <summary>
        /// Splits a long audio buffer into speech segments using Silero VAD.
        /// </summary>
        /// <param name="audio">16kHz float mono PCM audio.</param>
        /// <param name="threshold">VAD threshold. Default is 0.5.</param>
        /// <param name="minSpeechDurationMs">Minimum speech duration to keep a segment. Default is 250ms.</param>
        /// <param name="minSilenceDurationMs">Minimum silence duration to split segments. Default is 100ms.</param>
        /// <param name="maxSpeechDurationS">Maximum speech duration before force-splitting. 0 = disabled.</param>
        /// <param name="speechPadMs">Padding in ms added to start/end of each segment. Default is 30.</param>
        /// <returns>A list of speech segments containing start and end sample positions.</returns>
        public List<SpeechSegment> GetSpeechTimestamps(
            float[] audio,
            float threshold = 0.5f,
            int minSpeechDurationMs = 250,
            int minSilenceDurationMs = 100,
            float maxSpeechDurationS = 0f,
            int speechPadMs = 30)
        {
            ResetState();

            const int sampleRate = 16000;
            const int chunkSizeSamples = 512; // 32ms frames
            int minSpeechSamples = minSpeechDurationMs * sampleRate / 1000;
            int minSilenceSamples = minSilenceDurationMs * sampleRate / 1000;
            int maxSpeechSamples = maxSpeechDurationS > 0 ? (int)(maxSpeechDurationS * sampleRate) : 0;
            int speechPadSamples = speechPadMs * sampleRate / 1000;

            List<SpeechSegment> segments = new();
            bool triggered = false;
            long tempStart = 0;
            long tempEnd = 0;

            float[] chunk = new float[chunkSizeSamples];

            for (long offset = 0; offset < audio.Length; offset += chunkSizeSamples)
            {
                long remaining = audio.Length - offset;
                if (remaining < chunkSizeSamples)
                {
                    Array.Clear(chunk, 0, chunk.Length);
                    Array.Copy(audio, offset, chunk, 0, remaining);
                }
                else
                {
                    Array.Copy(audio, offset, chunk, 0, chunkSizeSamples);
                }

                float prob = ProcessChunk(chunk, sampleRate);

                if (prob >= threshold)
                {
                    if (!triggered)
                    {
                        triggered = true;
                        tempStart = offset;
                    }
                    tempEnd = offset + chunkSizeSamples;

                    // Force-split if segment exceeds max speech duration
                    if (maxSpeechSamples > 0 && (tempEnd - tempStart) >= maxSpeechSamples)
                    {
                        segments.Add(new SpeechSegment(tempStart, Math.Min(tempEnd, audio.Length)));
                        triggered = false;
                    }
                }
                else
                {
                    if (triggered)
                    {
                        long silenceDuration = offset - tempEnd;
                        if (silenceDuration > minSilenceSamples)
                        {
                            // Speech ended
                            triggered = false;
                            long duration = tempEnd - tempStart;
                            if (duration >= minSpeechSamples)
                            {
                                segments.Add(new SpeechSegment(tempStart, Math.Min(tempEnd, audio.Length)));
                            }
                        }
                    }
                }
            }

            // Handle final pending segment
            if (triggered)
            {
                long duration = audio.Length - tempStart;
                if (duration >= minSpeechSamples)
                {
                    segments.Add(new SpeechSegment(tempStart, audio.Length));
                }
            }

            // Apply speech padding: expand each segment by speechPadSamples on both sides
            if (speechPadSamples > 0 && segments.Count > 0)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    long paddedStart = Math.Max(0, segments[i].StartSample - speechPadSamples);
                    long paddedEnd = Math.Min(audio.Length, segments[i].EndSample + speechPadSamples);

                    // Clamp to avoid overlapping with adjacent segments
                    if (i > 0 && paddedStart < segments[i - 1].EndSample)
                    {
                        paddedStart = segments[i - 1].EndSample;
                    }
                    if (i < segments.Count - 1 && paddedEnd > segments[i + 1].StartSample)
                    {
                        paddedEnd = segments[i + 1].StartSample;
                    }

                    segments[i] = new SpeechSegment(paddedStart, paddedEnd);
                }
            }

            return segments;
        }

        /// <summary>
        /// Releases the ONNX Runtime inference session resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged (and optionally managed) resources.
        /// </summary>
        /// <param name="disposing">true if called from <see cref="Dispose()"/>; false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _session.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Ensures the ONNX Runtime session is freed if Dispose is not called.
        /// </summary>
        ~SileroVad()
        {
            Dispose(disposing: false);
        }
    }
}
