// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Fluent builder for configuring and loading a WhisperModel.
    /// Provides a clean, discoverable API for model configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// var model = await WhisperModelBuilder
    ///     .Create("large-v3")
    ///     .WithDevice("cuda")
    ///     .WithComputeType("float16")
    ///     .WithVad(threshold: 0.5f)
    ///     .WithWordTimestamps()
    ///     .WithDenoising()
    ///     .BuildAsync();
    /// </code>
    /// </example>
    public class WhisperModelBuilder
    {
        private string _model = "base";
        private string _device = "cpu";
        private string _computeType = "default";
        private int[] _deviceIndices = [0];
        private int _numReplicas = 1;
        private WhisperOptions _options = new();
        private VadOptions _vadOptions = new();
        private ILogger? _logger;
        private bool _useMemoryMapping = false;

        private WhisperModelBuilder() { }

        /// <summary>Creates a new builder with the specified model name.</summary>
        /// <param name="model">Model shorthand ("base", "small", "medium", "large-v3"), HuggingFace repo ID, or local path.</param>
        public static WhisperModelBuilder Create(string model)
        {
            return new WhisperModelBuilder { _model = model };
        }

        // ─── Device & Compute ───────────────────────────────────────────────
        /// <summary>Sets the compute device ("cpu" or "cuda").</summary>
        public WhisperModelBuilder WithDevice(string device)
        {
            _device = device;
            return this;
        }

        /// <summary>Sets the computation precision ("float32", "float16", "int16", "int8", "int8_float16", "default").</summary>
        public WhisperModelBuilder WithComputeType(string computeType)
        {
            _computeType = computeType;
            return this;
        }

        /// <summary>Sets an ILogger for diagnostic logging during model load and transcription.</summary>
        public WhisperModelBuilder WithLogger(ILogger logger)
        {
            _logger = logger;
            return this;
        }

        /// <summary>Sets the device indices for multi-GPU distribution.</summary>
        public WhisperModelBuilder WithDeviceIndices(params int[] indices)
        {
            _deviceIndices = indices;
            return this;
        }

        /// <summary>Sets the number of model replicas to load for concurrent inference.</summary>
        public WhisperModelBuilder WithNumReplicas(int numReplicas)
        {
            _numReplicas = numReplicas;
            return this;
        }

        // ─── VAD ────────────────────────────────────────────────────────────
        /// <summary>Enables VAD with specified threshold.</summary>
        public WhisperModelBuilder WithVad(float threshold = 0.5f, int minSpeechDurationMs = 250,
            int minSilenceDurationMs = 100, float maxSpeechDurationS = 0f, int speechPadMs = 30)
        {
            _vadOptions = new VadOptions
            {
                Enabled = true,
                Threshold = threshold,
                MinSpeechDurationMs = minSpeechDurationMs,
                MinSilenceDurationMs = minSilenceDurationMs,
                MaxSpeechDurationS = maxSpeechDurationS,
                SpeechPadMs = speechPadMs
            };
            return this;
        }

        // ─── Transcription Options ──────────────────────────────────────────
        /// <summary>Enables word-level timestamps.</summary>
        public WhisperModelBuilder WithWordTimestamps()
        {
            _options.WordTimestamps = true;
            return this;
        }

        /// <summary>Enables spectral noise gate denoising.</summary>
        public WhisperModelBuilder WithDenoising()
        {
            _options.DenoiseAudio = true;
            return this;
        }

        /// <summary>Enables RMS normalization of audio.</summary>
        public WhisperModelBuilder WithNormalization()
        {
            _options.NormalizeAudio = true;
            return this;
        }

        /// <summary>Enables high-pass filter to cut low frequencies.</summary>
        public WhisperModelBuilder WithHighPassFilter()
        {
            _options.CutLowFrequencies = true;
            return this;
        }

        /// <summary>Enables pre-emphasis filter.</summary>
        public WhisperModelBuilder WithPreEmphasis()
        {
            _options.PreEmphasis = true;
            return this;
        }

        /// <summary>Enables text formatting restoration (capitalization, punctuation).</summary>
        public WhisperModelBuilder WithTextRestoration()
        {
            _options.RestoreTextFormatting = true;
            return this;
        }

        /// <summary>Sets the beam size for decoding.</summary>
        public WhisperModelBuilder WithBeamSize(int beamSize)
        {
            _options.BeamSize = beamSize;
            return this;
        }

        /// <summary>Sets the number of candidates for sampling at non-zero temperature.</summary>
        public WhisperModelBuilder WithBestOf(int bestOf)
        {
            _options.BestOf = bestOf;
            return this;
        }

        /// <summary>Sets the initial prompt for context.</summary>
        public WhisperModelBuilder WithInitialPrompt(string prompt)
        {
            _options.InitialPrompt = prompt;
            return this;
        }

        /// <summary>Sets hotwords for vocabulary boosting.</summary>
        public WhisperModelBuilder WithHotwords(string hotwords)
        {
            _options.Hotwords = hotwords;
            return this;
        }

        /// <summary>Enables per-chunk multilingual detection.</summary>
        public WhisperModelBuilder WithMultilingual()
        {
            _options.Multilingual = true;
            return this;
        }

        /// <summary>Enables filler word filtering.</summary>
        public WhisperModelBuilder WithFillerWordFiltering()
        {
            _options.FilterFillerWords = true;
            return this;
        }

        /// <summary>Enables stutter pruning.</summary>
        public WhisperModelBuilder WithStutterPruning()
        {
            _options.PruneStutters = true;
            return this;
        }

        /// <summary>Enables multi-pass refinement for low-confidence segments.</summary>
        public WhisperModelBuilder WithMultiPass(float confidenceThreshold = 0.6f, int beamSize = 10)
        {
            _options.MultiPassEnabled = true;
            _options.MultiPassConfidenceThreshold = confidenceThreshold;
            _options.MultiPassBeamSize = beamSize;
            return this;
        }

        /// <summary>Sets the temperature fallback sequence.</summary>
        public WhisperModelBuilder WithTemperatures(params float[] temperatures)
        {
            _options.Temperatures = temperatures;
            return this;
        }

        /// <summary>Sets a custom WhisperOptions object directly.</summary>
        public WhisperModelBuilder WithOptions(WhisperOptions options)
        {
            _options = options;
            return this;
        }

        /// <summary>Sets a custom VadOptions object directly.</summary>
        public WhisperModelBuilder WithVadOptions(VadOptions vadOptions)
        {
            _vadOptions = vadOptions;
            return this;
        }

        // ─── Build ──────────────────────────────────────────────────────────
        /// <summary>Enables zero-copy memory-mapped file loading for weights.</summary>
        public WhisperModelBuilder WithMemoryMapping()
        {
            _useMemoryMapping = true;
            return this;
        }

        /// <summary>Gets the configured WhisperOptions.</summary>
        public WhisperOptions Options => _options;

        /// <summary>Gets the configured VadOptions.</summary>
        public VadOptions VadOpts => _vadOptions;

        /// <summary>
        /// Builds and loads the WhisperModel asynchronously.
        /// </summary>
        public async Task<WhisperModel> BuildAsync(CancellationToken cancellationToken = default)
        {
            if (_useMemoryMapping)
            {
                return WhisperModel.LoadMemoryMapped(_model, _device, _computeType, _deviceIndices,
                    cpuThreads: 4, flashAttention: false, numReplicas: _numReplicas, logger: _logger);
            }
            return await WhisperModel.LoadAsync(_model, _device, _computeType, _deviceIndices,
                    numReplicas: _numReplicas, cancellationToken: cancellationToken, logger: _logger)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Builds and loads the WhisperModel synchronously.
        /// </summary>
        public WhisperModel Build()
        {
            if (_useMemoryMapping)
            {
                return WhisperModel.LoadMemoryMapped(_model, _device, _computeType, _deviceIndices,
                    cpuThreads: 4, flashAttention: false, numReplicas: _numReplicas, logger: _logger);
            }
            return new WhisperModel(_model, _device, _computeType, _deviceIndices, numReplicas: _numReplicas, logger: _logger);
        }
    }
}
