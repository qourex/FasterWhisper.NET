// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Incrementally computes Mel spectrogram columns as audio samples arrive,
    /// reducing streaming latency by not buffering a full 30-second chunk.
    /// </summary>
    /// <remarks>
    /// <para>The standard Whisper pipeline buffers 30 seconds of audio before computing
    /// the Mel spectrogram. This class enables feeding audio incrementally and extracting
    /// Mel features as soon as enough samples accumulate for a new column.</para>
    /// <para>Each Mel column requires one FFT window worth of samples (typically 400 samples
    /// at 16kHz = 25ms). Columns are produced at the hop rate (typically 160 samples = 10ms).</para>
    /// </remarks>
    public class StreamingMelExtractor : IDisposable
    {
        private const int DefaultFftSize = 400;
        private const int DefaultHopLength = 160;
        private const int DefaultSampleRate = 16000;
        private const int FullFrameColumns = 3000; // 30 seconds at 10ms hop
        private const int FFTSize = 512; // Next power-of-2 >= 400

        private readonly int _nMels;
        private readonly int _fftSize;
        private readonly int _hopLength;
        private readonly float[] _slidingWindow;
        private int _writePos;
        private int _samplesProcessed;
        private int _columnsEmitted;
        private bool _disposed;

        // Scratch buffers for zero-allocation column extraction
        private readonly float[] _hannWindow;
        private readonly float[][] _melFilters;
        private readonly float[] _fftReal;
        private readonly float[] _fftImag;
        private readonly float[] _magnitudes;

        /// <summary>
        /// Creates a new StreamingMelExtractor.
        /// </summary>
        /// <param name="nMels">Number of Mel frequency bins (typically 80 or 128).</param>
        /// <param name="fftSize">FFT window size in samples. Default 400 (25ms at 16kHz).</param>
        /// <param name="hopLength">Hop length in samples. Default 160 (10ms at 16kHz).</param>
        public StreamingMelExtractor(int nMels = 80, int fftSize = DefaultFftSize, int hopLength = DefaultHopLength)
        {
            _nMels = nMels;
            _fftSize = fftSize;
            _hopLength = hopLength;
            _slidingWindow = new float[fftSize * 2]; // Double buffer for overlap
            _writePos = 0;
            _samplesProcessed = 0;
            _columnsEmitted = 0;

            // Periodic Hann window
            _hannWindow = new float[_fftSize];
            for (int i = 0; i < _fftSize; i++)
            {
                _hannWindow[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / _fftSize));
            }

            // Build Mel filters matching AudioProcessor exactly
            _melFilters = BuildMelFilters(DefaultSampleRate, _fftSize, _nMels);

            _fftReal = new float[FFTSize];
            _fftImag = new float[FFTSize];
            _magnitudes = new float[FFTSize / 2 + 1];
        }

        /// <summary>
        /// Feed audio samples. Returns the number of new Mel columns produced.
        /// </summary>
        /// <param name="samples">Input audio samples at 16kHz.</param>
        /// <param name="melOutput">
        /// Output buffer for Mel features. Must be large enough for
        /// (returned columns) * nMels floats. If null, columns are counted but not stored.
        /// </param>
        /// <returns>Number of new Mel columns produced.</returns>
        public int Feed(ReadOnlySpan<float> samples, Span<float> melOutput)
        {
            ThrowIfDisposed();
            int newColumns = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                _slidingWindow[_writePos % _slidingWindow.Length] = samples[i];
                _writePos++;
                _samplesProcessed++;

                // Check if we have enough samples for a new column
                if (_samplesProcessed >= _fftSize && (_samplesProcessed - _fftSize) % _hopLength == 0)
                {
                    // Extract the current window for FFT
                    if (!melOutput.IsEmpty)
                    {
                        int outputOffset = newColumns * _nMels;
                        if (outputOffset + _nMels <= melOutput.Length)
                        {
                            ExtractMelColumn(melOutput.Slice(outputOffset, _nMels));
                        }
                    }
                    newColumns++;
                    _columnsEmitted++;
                }
            }

            return newColumns;
        }

        /// <summary>
        /// Flush remaining samples and emit final columns (zero-padded if needed).
        /// </summary>
        /// <param name="melOutput">Output buffer for remaining Mel features.</param>
        /// <returns>Number of final Mel columns produced.</returns>
        public int Flush(Span<float> melOutput)
        {
            ThrowIfDisposed();

            // Calculate remaining samples that haven't formed a column
            int pendingSamples = _samplesProcessed < _fftSize
                ? _samplesProcessed
                : (_samplesProcessed - _fftSize) % _hopLength;

            if (pendingSamples == 0) return 0;

            // Zero-pad to complete the last column
            int padNeeded = _hopLength - pendingSamples;
            Span<float> padding = stackalloc float[Math.Min(padNeeded, 1024)];
            padding.Clear();

            int remaining = padNeeded;
            int totalColumns = 0;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, padding.Length);
                totalColumns += Feed(padding.Slice(0, chunk), melOutput);
                remaining -= chunk;
            }

            return totalColumns;
        }

        /// <summary>True when enough columns have been emitted for a full encoder input (3000 frames = 30s).</summary>
        public bool HasFullFrame => _columnsEmitted >= FullFrameColumns;

        /// <summary>Total Mel columns emitted so far.</summary>
        public int ColumnsEmitted => _columnsEmitted;

        /// <summary>Total audio samples processed so far.</summary>
        public int SamplesProcessed => _samplesProcessed;

        /// <summary>Duration of processed audio in seconds.</summary>
        public float ProcessedDurationSeconds => (float)_samplesProcessed / DefaultSampleRate;

        /// <summary>Reset state for a new audio stream.</summary>
        public void Reset()
        {
            ThrowIfDisposed();
            Array.Clear(_slidingWindow);
            _writePos = 0;
            _samplesProcessed = 0;
            _columnsEmitted = 0;
        }

        private void ExtractMelColumn(Span<float> output)
        {
            int numBins = _fftSize / 2 + 1; // 201 bins

            // 1. Extract and window samples: frame[i] = sample * hannWindow[i]
            int startIdx = (_writePos - _fftSize + _slidingWindow.Length) % _slidingWindow.Length;
            
            Array.Clear(_fftReal, 0, FFTSize);
            Array.Clear(_fftImag, 0, FFTSize);
            
            int vecSize2 = Vector<float>.Count;
            int wi = 0;
            
            // Copy circular buffer to linear stackalloc array for vectorization
            Span<float> frame = stackalloc float[_fftSize];
            for (int i = 0; i < _fftSize; i++)
            {
                frame[i] = _slidingWindow[(startIdx + i) % _slidingWindow.Length];
            }

            // Windowing using SIMD
            for (; wi <= _fftSize - vecSize2; wi += vecSize2)
            {
                var vSrc = new Vector<float>(frame.Slice(wi));
                var vWin = new Vector<float>(_hannWindow, wi);
                (vSrc * vWin).CopyTo(_fftReal, wi);
            }
            for (; wi < _fftSize; wi++)
            {
                _fftReal[wi] = frame[wi] * _hannWindow[wi];
            }

            // 2. Compute in-place FFT using the optimized AudioProcessor implementation
            AudioProcessor.FFTInPlace(_fftReal, _fftImag, FFTSize);

            // 3. Compute magnitude squared
            int mi = 0;
            for (; mi <= numBins - vecSize2; mi += vecSize2)
            {
                var vR = new Vector<float>(_fftReal, mi);
                var vI = new Vector<float>(_fftImag, mi);
                (vR * vR + vI * vI).CopyTo(_magnitudes, mi);
            }
            for (; mi < numBins; mi++)
            {
                _magnitudes[mi] = _fftReal[mi] * _fftReal[mi] + _fftImag[mi] * _fftImag[mi];
            }

            // 4. Apply Mel filters
            int vecSize = Vector<float>.Count;
            for (int m = 0; m < _nMels && m < output.Length; m++)
            {
                float melValue = 0f;
                float[] filter = _melFilters[m];

                // SIMD vectorized dot product
                int k = 0;
                for (; k <= numBins - vecSize; k += vecSize)
                {
                    var filterVec = new Vector<float>(filter, k);
                    var magVec = new Vector<float>(_magnitudes, k);
                    melValue += Vector.Dot(filterVec, magVec);
                }
                // Scalar remainder
                for (; k < numBins; k++)
                {
                    melValue += filter[k] * _magnitudes[k];
                }

                // Store log Mel
                output[m] = (float)Math.Log10(Math.Max(melValue, 1e-10));
            }
        }

        private static float[][] BuildMelFilters(int sr, int nFft, int nMels)
        {
            int numBins = nFft / 2 + 1; // 201

            // FFT bin center frequencies: np.fft.rfftfreq(n=n_fft, d=1.0/sr)
            // = k * sr / n_fft for k = 0..numBins-1
            double[] fftfreqs = new double[numBins];
            for (int k = 0; k < numBins; k++)
            {
                fftfreqs[k] = (double)k * sr / nFft;
            }

            // Mel scale points: np.linspace(min_mel, max_mel, n_mels + 2)
            double minMel = 0.0;
            double maxMel = 45.245640471924965;

            double[] mels = new double[nMels + 2];
            for (int i = 0; i < nMels + 2; i++)
            {
                mels[i] = minMel + i * (maxMel - minMel) / (nMels + 1);
            }

            // Convert mels to Hz using Slaney scale
            double fMin = 0.0;
            double fSp = 200.0 / 3.0;
            double minLogHz = 1000.0;
            double minLogMel = (minLogHz - fMin) / fSp;
            double logstep = Math.Log(6.4) / 27.0;

            double[] freqs = new double[nMels + 2];
            for (int i = 0; i < nMels + 2; i++)
            {
                if (mels[i] >= minLogMel)
                {
                    freqs[i] = minLogHz * Math.Exp(logstep * (mels[i] - minLogMel));
                }
                else
                {
                    freqs[i] = fMin + fSp * mels[i];
                }
            }

            // fdiff = np.diff(freqs)
            double[] fdiff = new double[nMels + 1];
            for (int i = 0; i < nMels + 1; i++)
            {
                fdiff[i] = freqs[i + 1] - freqs[i];
            }

            // ramps = freqs.reshape(-1,1) - fftfreqs.reshape(1,-1)
            // lower = -ramps[:-2] / fdiff[:-1]
            // upper = ramps[2:] / fdiff[1:]
            // weights = max(0, min(lower, upper))
            float[][] weights = new float[nMels][];
            for (int m = 0; m < nMels; m++)
            {
                weights[m] = new float[numBins];
                for (int k = 0; k < numBins; k++)
                {
                    double lower = -(freqs[m] - fftfreqs[k]) / fdiff[m];
                    double upper = (freqs[m + 2] - fftfreqs[k]) / fdiff[m + 1];
                    double val = Math.Max(0.0, Math.Min(lower, upper));
                    weights[m][k] = (float)val;
                }

                // Slaney normalization: enorm = 2.0 / (freqs[m+2] - freqs[m])
                double enorm = 2.0 / (freqs[m + 2] - freqs[m]);
                for (int k = 0; k < numBins; k++)
                {
                    weights[m][k] *= (float)enorm;
                }
            }

            return weights;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StreamingMelExtractor));
        }

        /// <summary>Disposes resources.</summary>
        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
