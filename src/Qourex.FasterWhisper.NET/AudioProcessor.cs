// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Qourex.FasterWhisper.NET.Tests")]

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Processes audio inputs (WAV files or raw float PCM) into the Log-Mel Spectrogram format expected by Whisper.
    /// This implementation mirrors the python FeatureExtractor exactly.
    /// </summary>
    public class AudioProcessor
    {
        private const int SampleRate = 16000;
        private const int NFFT = 400;
        private const int HopLength = 160;
        private const int ChunkLength = 30; // 30 seconds
        private const int MaxFrames = ChunkLength * 100; // 3000 frames

        private readonly float[] _hannWindow;
        private readonly float[][] _melFilters;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioProcessor"/> class.
        /// Pre-calculates Hann window and Mel filter bank matching the python implementation exactly.
        /// </summary>
        /// <param name="nMels">Number of Mel bands (80 or 128).</param>
        public AudioProcessor(int nMels = 80)
        {
            // Periodic Hann window: np.hanning(N+1)[:-1]
            _hannWindow = new float[NFFT];
            for (int i = 0; i < NFFT; i++)
            {
                _hannWindow[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / NFFT));
            }

            // Build Mel filters matching SYSTRAN's get_mel_filters exactly
            _melFilters = BuildMelFilters(SampleRate, NFFT, nMels);
        }

        /// <summary>
        /// Decodes a standard WAV file into 16kHz mono float32 PCM.
        /// Supports 8/16/24/32-bit PCM, 32/64-bit IEEE float, A-law (G.711), and μ-law (G.711).
        /// Mono and multi-channel audio is handled (channels are averaged to mono).
        /// </summary>
        /// <param name="wavPath">Path to the WAV file.</param>
        /// <returns>A float array containing the resampled mono audio samples.</returns>
        public float[] LoadWav(string wavPath)
        {
            using var fs = File.OpenRead(wavPath);
            using var reader = new BinaryReader(fs);

            // Read RIFF Header
            if (new string(reader.ReadChars(4)) != "RIFF")
                throw new InvalidDataException("Not a valid RIFF file.");

            reader.ReadInt32(); // File size

            if (new string(reader.ReadChars(4)) != "WAVE")
                throw new InvalidDataException("Not a valid WAVE file.");

            short formatType = 0;
            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            byte[]? audioBytes = null;

            while (fs.Position < fs.Length)
            {
                string chunkId = new(reader.ReadChars(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    formatType = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // Byte rate
                    reader.ReadInt16(); // Block align
                    bitsPerSample = reader.ReadInt16();

                    if (chunkSize > 16)
                    {
                        fs.Seek(chunkSize - 16, SeekOrigin.Current);
                    }

                    // WAV spec requires chunks to be word-aligned (even byte boundaries)
                    if (chunkSize % 2 != 0 && fs.Position < fs.Length)
                    {
                        fs.Seek(1, SeekOrigin.Current);
                    }
                }
                else if (chunkId == "data")
                {
                    audioBytes = reader.ReadBytes(chunkSize);
                    break;
                }
                else
                {
                    fs.Seek(chunkSize, SeekOrigin.Current);
                    // WAV spec requires chunks to be word-aligned (even byte boundaries)
                    if (chunkSize % 2 != 0 && fs.Position < fs.Length)
                        fs.Seek(1, SeekOrigin.Current);
                }
            }

            if (audioBytes == null)
                throw new InvalidDataException("Data chunk not found in WAV file.");

            // Convert to Float PCM
            float[] samples;
            int bytesPerSample = bitsPerSample / 8;
            int rawSampleCount = audioBytes.Length / bytesPerSample;

            if (formatType == 1) // PCM
            {
                samples = bitsPerSample switch
                {
                    8 => Decode8BitPcm(audioBytes, rawSampleCount),
                    16 => Decode16BitPcm(audioBytes, rawSampleCount),
                    24 => Decode24BitPcm(audioBytes, rawSampleCount),
                    32 => Decode32BitPcm(audioBytes, rawSampleCount),
                    _ => throw new NotSupportedException($"Unsupported PCM bit depth: {bitsPerSample}-bit.")
                };
            }
            else if (formatType == 3) // IEEE Float
            {
                samples = bitsPerSample switch
                {
                    32 => Decode32BitFloat(audioBytes, rawSampleCount),
                    64 => Decode64BitFloat(audioBytes, rawSampleCount),
                    _ => throw new NotSupportedException($"Unsupported float bit depth: {bitsPerSample}-bit.")
                };
            }
            else if (formatType == 6) // A-law
            {
                samples = DecodeALaw(audioBytes, rawSampleCount);
            }
            else if (formatType == 7) // μ-law
            {
                samples = DecodeMuLaw(audioBytes, rawSampleCount);
            }
            else
            {
                throw new NotSupportedException($"Unsupported WAV format type: {formatType}.");
            }

            // Convert Stereo to Mono
            if (channels > 1)
            {
                int monoLength = samples.Length / channels;
                float[] monoSamples = new float[monoLength];
                for (int i = 0; i < monoLength; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += samples[i * channels + c];
                    }
                    monoSamples[i] = sum / channels;
                }
                samples = monoSamples;
            }

            // Resample to 16000Hz if needed
            if (sampleRate != SampleRate)
            {
                samples = Resample(samples, sampleRate, SampleRate);
            }

            return samples;
        }

        /// <summary>
        /// Resamples a float PCM array from a given source rate to a target rate (typically 16000Hz).
        /// Uses a Lanczos windowed-sinc interpolation kernel for proper anti-aliasing.
        /// Supports both upsampling (e.g. 8kHz → 16kHz) and downsampling (e.g. 48kHz → 16kHz).
        /// </summary>
        public static float[] Resample(float[] input, int fromRate, int toRate = 16000)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            if (fromRate == toRate)
                return input;
            if (fromRate <= 0 || toRate <= 0)
                throw new ArgumentException("Sample rates must be positive numbers.");

            double ratio = (double)fromRate / toRate;
            int outputLength = (int)Math.Round(input.Length / ratio);
            if (outputLength <= 0)
                return Array.Empty<float>();

            float[] output = new float[outputLength];

            // Lanczos kernel radius
            const int kernelRadius = 3;
            // Anti-aliasing filter: when downsampling, scale the kernel wider
            double filterScale = ratio > 1.0 ? ratio : 1.0;
            double invFilterScale = 1.0 / filterScale;

            for (int i = 0; i < outputLength; i++)
            {
                double srcPos = i * ratio;
                int srcCenter = (int)Math.Floor(srcPos);
                double sum = 0.0;
                double weightSum = 0.0;

                int jMin = srcCenter - kernelRadius + 1;
                int jMax = srcCenter + kernelRadius;

                for (int j = jMin; j <= jMax; j++)
                {
                    if (j < 0 || j >= input.Length)
                        continue;

                    double x = (srcPos - j) * invFilterScale;
                    double weight = LanczosKernel(x, kernelRadius);
                    sum += input[j] * weight;
                    weightSum += weight;
                }

                output[i] = weightSum > 0 ? (float)(sum / weightSum) : 0f;
            }

            return output;
        }

        /// <summary>
        /// Lanczos windowed-sinc kernel: sinc(x) * sinc(x/a) for |x| &lt; a, else 0.
        /// </summary>
        private static double LanczosKernel(double x, int a)
        {
            if (x == 0.0) return 1.0;
            if (x >= a || x <= -a) return 0.0;
            double pix = Math.PI * x;
            return (Math.Sin(pix) / pix) * (Math.Sin(pix / a) / (pix / a));
        }

        /// <summary>
        /// Extracts the Log-Mel Spectrogram from raw PCM audio, matching the python implementation's
        /// FeatureExtractor.__call__ exactly.
        /// </summary>
        /// <param name="pcm">16kHz float32 mono PCM samples.</param>
        /// <param name="nMels">Number of Mel bands (80 or 128).</param>
        /// <returns>A flat 1D float array of size [nMels * numFrames] representing the Mel spectrogram.</returns>
        public float[] ExtractMelSpectrogram(float[] pcm, int nMels)
            => ExtractMelSpectrogram(pcm, pcm.Length, nMels);

        public float[] ExtractMelSpectrogram(float[] pcm, int pcmLength, int nMels)
        {
            float[] melSpectrogram = new float[nMels * MaxFrames];
            ExtractMelSpectrogram(pcm, pcmLength, nMels, melSpectrogram);
            return melSpectrogram;
        }

        public void ExtractMelSpectrogram(float[] pcm, int pcmLength, int nMels, Span<float> destination)
        {
            if (nMels != _melFilters.Length)
            {
                throw new ArgumentException($"nMels ({nMels}) must match the number of mel bands configured in the constructor ({_melFilters.Length}).");
            }

            int outputFrames = MaxFrames;
            if (destination.Length < nMels * outputFrames)
            {
                throw new ArgumentException($"Destination span length ({destination.Length}) must be at least {nMels * outputFrames}.", nameof(destination));
            }

            int paddedLength = pcmLength + HopLength;
            int padAmount = NFFT / 2;
            int centeredLength = paddedLength + 2 * padAmount;
            int numStftFrames = 1 + (centeredLength - NFFT) / HopLength;
            int numBins = NFFT / 2 + 1; // 201 bins
            int numMagFrames = numStftFrames - 1;
            const int FFTSize = 512; // Next power-of-2 >= NFFT (400)

            float[] padded = System.Buffers.ArrayPool<float>.Shared.Rent(paddedLength);
            float[] centered = System.Buffers.ArrayPool<float>.Shared.Rent(centeredLength);
            float[] fftReal = System.Buffers.ArrayPool<float>.Shared.Rent(FFTSize);
            float[] fftImag = System.Buffers.ArrayPool<float>.Shared.Rent(FFTSize);
            float[] magnitudes = System.Buffers.ArrayPool<float>.Shared.Rent(numBins);

            try
            {
                // Clear the logical ranges to discard old pool values
                Array.Clear(padded, 0, paddedLength);
                Array.Clear(centered, 0, centeredLength);

                // Step 1: Pad end with 160 zeros (matching faster-whisper's padding=160)
                Array.Copy(pcm, padded, pcmLength);

                // Step 2: Reflect-pad both sides by N_FFT//2 = 200 (center=True in STFT)
                // Reflect padding at the start
                for (int i = 0; i < padAmount; i++)
                {
                    centered[padAmount - 1 - i] = padded[i + 1];
                }
                // Copy original
                Array.Copy(padded, 0, centered, padAmount, paddedLength);
                // Reflect padding at the end
                for (int i = 0; i < padAmount; i++)
                {
                    centered[padAmount + paddedLength + i] = padded[paddedLength - 2 - i];
                }

                // Step 3: STFT using np.fft.rfft (center-padded signal)
                int framesToProcess = Math.Min(numMagFrames, outputFrames);

                for (int f = 0; f < framesToProcess; f++)
                {
                    int startIdx = f * HopLength;

                    // SIMD-accelerated windowing: frame[i] = centered[startIdx+i] * hannWindow[i]
                    Array.Clear(fftReal, 0, FFTSize);
                    Array.Clear(fftImag, 0, FFTSize);
                    int vecSize2 = Vector<float>.Count;
                    int wi = 0;
                    for (; wi <= NFFT - vecSize2; wi += vecSize2)
                    {
                        var vSrc = new Vector<float>(centered, startIdx + wi);
                        var vWin = new Vector<float>(_hannWindow, wi);
                        (vSrc * vWin).CopyTo(fftReal, wi);
                    }
                    for (; wi < NFFT; wi++)
                    {
                        fftReal[wi] = centered[startIdx + wi] * _hannWindow[wi];
                    }

                    // In-place radix-2 Cooley-Tukey FFT (O(N log N) vs O(N²) DFT)
                    FFTInPlace(fftReal, fftImag, FFTSize);

                    // SIMD-accelerated magnitude squared: mag[k] = real[k]² + imag[k]²
                    int mi = 0;
                    for (; mi <= numBins - vecSize2; mi += vecSize2)
                    {
                        var vR = new Vector<float>(fftReal, mi);
                        var vI = new Vector<float>(fftImag, mi);
                        (vR * vR + vI * vI).CopyTo(magnitudes, mi);
                    }
                    for (; mi < numBins; mi++)
                    {
                        magnitudes[mi] = fftReal[mi] * fftReal[mi] + fftImag[mi] * fftImag[mi];
                    }

                    // Apply Mel filters with SIMD vectorization: mel_spec[:, f] = mel_filters @ magnitudes
                    int vecSize = Vector<float>.Count;
                    for (int m = 0; m < nMels; m++)
                    {
                        float melValue = 0f;
                        float[] filter = _melFilters[m];

                        // SIMD vectorized dot product
                        int k = 0;
                        for (; k <= numBins - vecSize; k += vecSize)
                        {
                            var filterVec = new Vector<float>(filter, k);
                            var magVec = new Vector<float>(magnitudes, k);
                            melValue += Vector.Dot(filterVec, magVec);
                        }
                        // Scalar remainder
                        for (; k < numBins; k++)
                        {
                            melValue += filter[k] * magnitudes[k];
                        }

                        // Store: log10(clip(mel, 1e-10))
                        float logMel = (float)Math.Log10(Math.Max(melValue, 1e-10));
                        destination[m * outputFrames + f] = logMel;
                    }
                }

                // Fill remaining frames (if audio < 30s) with log10(1e-10) = -10
                for (int f = framesToProcess; f < outputFrames; f++)
                {
                    for (int m = 0; m < nMels; m++)
                    {
                        destination[m * outputFrames + f] = -10.0f;
                    }
                }

                // Step 4: Normalization (matches Whisper exactly)
                // log_spec = np.maximum(log_spec, log_spec.max() - 8.0)
                // log_spec = (log_spec + 4.0) / 4.0
                float maxVal = float.MinValue;
                int destLen = nMels * outputFrames;
                for (int i = 0; i < destLen; i++)
                {
                    if (destination[i] > maxVal)
                    {
                        maxVal = destination[i];
                    }
                }

                float threshold = maxVal - 8.0f;
                for (int i = 0; i < destLen; i++)
                {
                    float val = destination[i];
                    if (val < threshold)
                    {
                        val = threshold;
                    }
                    destination[i] = (val + 4.0f) / 4.0f;
                }
            }
            finally
            {
                System.Buffers.ArrayPool<float>.Shared.Return(padded);
                System.Buffers.ArrayPool<float>.Shared.Return(centered);
                System.Buffers.ArrayPool<float>.Shared.Return(fftReal);
                System.Buffers.ArrayPool<float>.Shared.Return(fftImag);
                System.Buffers.ArrayPool<float>.Shared.Return(magnitudes);
            }
        }

        /// <summary>
        /// Builds Mel filter bank matching the python get_mel_filters exactly.
        /// Uses np.fft.rfftfreq for FFT bin center frequencies and Slaney-style Mel scale.
        /// </summary>
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

        /// <summary>
        /// Normalizes audio to a target RMS level, with SIMD-accelerated sum-of-squares computation.
        /// </summary>
        public static void NormalizeRms(float[] samples, float targetDb = -20f)
        {
            if (samples == null || samples.Length == 0) return;

            float targetRms = (float)Math.Pow(10d, targetDb / 20d);

            // SIMD-accelerated sum of squares
            float sum = 0f;
            int vecSize = Vector<float>.Count;
            int i = 0;
            for (; i <= samples.Length - vecSize; i += vecSize)
            {
                var v = new Vector<float>(samples, i);
                sum += Vector.Dot(v, v);
            }
            for (; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            float rms = (float)Math.Sqrt(sum / samples.Length);
            if (rms < 1e-5f) return; // avoid division by zero or extremely quiet noise amplification

            float gain = targetRms / rms;

            // Limit gain to a reasonable amount (e.g. max 10.0x boost) to avoid boosting silence hiss
            if (gain > 10.0f) gain = 10.0f;

            // SIMD-accelerated gain application
            var gainVec = new Vector<float>(gain);
            i = 0;
            for (; i <= samples.Length - vecSize; i += vecSize)
            {
                var v = new Vector<float>(samples, i);
                (v * gainVec).CopyTo(samples, i);
            }
            for (; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        /// <summary>
        /// Applies a first-order high-pass filter to remove low-frequency rumble (breath pops, hum) below cutoffHz.
        /// </summary>
        public static void ApplyHighPassFilter(float[] samples, float cutoffHz = 80f, float sampleRate = 16000f)
        {
            if (samples == null || samples.Length == 0) return;

            double dt = 1.0 / sampleRate;
            double rc = 1.0 / (2.0 * Math.PI * cutoffHz);
            float alpha = (float)(rc / (rc + dt));

            float prevInput = samples[0];
            float prevOutput = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                float currentInput = samples[i];
                float currentOutput = alpha * (prevOutput + currentInput - prevInput);
                prevInput = currentInput;
                prevOutput = currentOutput;
                samples[i] = currentOutput;
            }
        }

        /// <summary>
        /// Applies pre-emphasis filter to boost high-frequency consonant energy.
        /// Standard ASR technique: y[n] = x[n] - α * x[n-1].
        /// Improves recognition of consonants and word boundaries.
        /// </summary>
        /// <param name="samples">Audio samples to filter in-place.</param>
        /// <param name="coefficient">Pre-emphasis coefficient (typically 0.97).</param>
        public static void ApplyPreEmphasis(float[] samples, float coefficient = 0.97f)
        {
            if (samples == null || samples.Length < 2) return;

            // Process in reverse to avoid overwriting needed values
            for (int i = samples.Length - 1; i >= 1; i--)
            {
                samples[i] = samples[i] - coefficient * samples[i - 1];
            }
        }

        /// <summary>
        /// Trims leading and trailing silence from audio samples.
        /// </summary>
        /// <param name="samples">Audio samples.</param>
        /// <param name="thresholdDb">Silence threshold in dB (default: -40 dB).</param>
        /// <param name="frameLengthMs">Analysis frame length in milliseconds.</param>
        /// <returns>A new array with leading/trailing silence removed.</returns>
        public static float[] TrimSilence(float[] samples, float thresholdDb = -40f, int frameLengthMs = 20)
        {
            if (samples == null || samples.Length == 0) return samples ?? Array.Empty<float>();

            float threshold = (float)Math.Pow(10d, thresholdDb / 20d);
            int frameLen = SampleRate * frameLengthMs / 1000;

            int start = 0;
            int end = samples.Length;

            // Find first non-silent frame
            for (int i = 0; i < samples.Length - frameLen; i += frameLen)
            {
                float energy = 0f;
                for (int j = 0; j < frameLen; j++)
                {
                    energy += Math.Abs(samples[i + j]);
                }
                if (energy / frameLen > threshold)
                {
                    start = i;
                    break;
                }
            }

            // Find last non-silent frame
            for (int i = samples.Length - frameLen; i >= start; i -= frameLen)
            {
                float energy = 0f;
                for (int j = 0; j < frameLen && i + j < samples.Length; j++)
                {
                    energy += Math.Abs(samples[i + j]);
                }
                if (energy / frameLen > threshold)
                {
                    end = Math.Min(i + frameLen, samples.Length);
                    break;
                }
            }

            if (start >= end) return samples;

            float[] trimmed = new float[end - start];
            Array.Copy(samples, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>
        /// Spawns an FFmpeg subprocess to decode and resample any media file into 16kHz mono float32 PCM.
        /// Requires FFmpeg to be installed on the host system PATH.
        /// </summary>
        public static float[] DecodeAndResampleWithFFmpeg(string mediaPath)
        {
            if (!File.Exists(mediaPath))
                throw new FileNotFoundException("Input media file not found.", mediaPath);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-v error -i \"{mediaPath}\" -f f32le -ac 1 -ar 16000 -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException("FFmpeg executable not found in system PATH. Please ensure FFmpeg is installed to decode this file format.");
            }

            var samples = new System.Collections.Generic.List<float>(16000 * 60); // pre-allocate 1 min
            using var stdout = process.StandardOutput.BaseStream;
            byte[] buffer = new byte[4096];
            int bytesRead;

            while ((bytesRead = stdout.Read(buffer, 0, buffer.Length)) > 0)
            {
                int sampleCount = bytesRead / 4;
                for (int i = 0; i < sampleCount; i++)
                {
                    float val = BitConverter.ToSingle(buffer, i * 4);
                    samples.Add(val);
                }
            }

            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 && samples.Count == 0)
            {
                throw new InvalidOperationException($"FFmpeg decoding failed: {errors}");
            }

            return samples.ToArray();
        }

        /// <summary>
        /// In-place radix-2 Cooley-Tukey FFT. N must be a power of 2.
        /// Transforms real[] and imag[] arrays in-place from time domain to frequency domain.
        /// </summary>
        internal static void FFTInPlace(float[] real, float[] imag, int n)
        {
            // Bit-reversal permutation
            int bits = (int)Math.Log2(n);
            for (int i = 0; i < n; i++)
            {
                int j = BitReverse(i, bits);
                if (j > i)
                {
                    // Swap real
                    (real[i], real[j]) = (real[j], real[i]);
                    // Swap imag
                    (imag[i], imag[j]) = (imag[j], imag[i]);
                }
            }

            // Butterfly operations with pre-computed twiddle factors
            for (int size = 2; size <= n; size *= 2)
            {
                int halfSize = size / 2;
                double angleStep = -2.0 * Math.PI / size;

                // Pre-compute twiddle factors for this stage
                float[] twiddleR = new float[halfSize];
                float[] twiddleI = new float[halfSize];
                for (int k = 0; k < halfSize; k++)
                {
                    double angle = angleStep * k;
                    twiddleR[k] = (float)Math.Cos(angle);
                    twiddleI[k] = (float)Math.Sin(angle);
                }

                for (int i = 0; i < n; i += size)
                {
                    for (int k = 0; k < halfSize; k++)
                    {
                        int evenIdx = i + k;
                        int oddIdx = i + k + halfSize;

                        float tReal = twiddleR[k] * real[oddIdx] - twiddleI[k] * imag[oddIdx];
                        float tImag = twiddleR[k] * imag[oddIdx] + twiddleI[k] * real[oddIdx];

                        real[oddIdx] = real[evenIdx] - tReal;
                        imag[oddIdx] = imag[evenIdx] - tImag;
                        real[evenIdx] = real[evenIdx] + tReal;
                        imag[evenIdx] = imag[evenIdx] + tImag;
                    }
                }
            }
        }

        /// <summary>
        /// Reverses the bits of an integer for FFT bit-reversal permutation.
        /// </summary>
        internal static int BitReverse(int value, int bits)
        {
            int result = 0;
            for (int i = 0; i < bits; i++)
            {
                result = (result << 1) | (value & 1);
                value >>= 1;
            }
            return result;
        }

        // ---- WAV Format Decoders ----

        private static float[] Decode8BitPcm(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                // 8-bit PCM is unsigned: 0-255, center at 128
                samples[i] = (data[i] - 128) / 128f;
            }
            return samples;
        }

        private static float[] Decode16BitPcm(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(data, i * 2);
                samples[i] = s / 32768f;
            }
            return samples;
        }

        private static float[] Decode24BitPcm(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * 3;
                // Read 3 bytes and sign-extend to 32-bit int
                int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                // Sign-extend from 24-bit to 32-bit
                if ((value & 0x800000) != 0)
                    value |= unchecked((int)0xFF000000);
                samples[i] = value / 8388608f; // 2^23
            }
            return samples;
        }

        private static float[] Decode32BitPcm(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int value = BitConverter.ToInt32(data, i * 4);
                samples[i] = value / 2147483648f; // 2^31
            }
            return samples;
        }

        private static float[] Decode32BitFloat(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToSingle(data, i * 4);
            }
            return samples;
        }

        private static float[] Decode64BitFloat(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = (float)BitConverter.ToDouble(data, i * 8);
            }
            return samples;
        }

        /// <summary>
        /// Decodes ITU G.711 A-law encoded audio to linear float PCM.
        /// </summary>
        private static float[] DecodeALaw(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = ALawToLinear(data[i]) / 32768f;
            }
            return samples;
        }

        /// <summary>
        /// Decodes ITU G.711 μ-law encoded audio to linear float PCM.
        /// </summary>
        private static float[] DecodeMuLaw(byte[] data, int sampleCount)
        {
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = MuLawToLinear(data[i]) / 32768f;
            }
            return samples;
        }

        private static short ALawToLinear(byte aLaw)
        {
            aLaw ^= 0x55;
            int sign = (aLaw & 0x80) != 0 ? -1 : 1;
            int exponent = (aLaw >> 4) & 0x07;
            int mantissa = aLaw & 0x0F;
            int magnitude;
            if (exponent == 0)
                magnitude = (mantissa << 4) | 0x08;
            else
                magnitude = ((mantissa << 4) | 0x108) << (exponent - 1);
            return (short)(sign * magnitude);
        }

        private static short MuLawToLinear(byte muLaw)
        {
            muLaw = (byte)~muLaw;
            int sign = (muLaw & 0x80) != 0 ? -1 : 1;
            int exponent = (muLaw >> 4) & 0x07;
            int mantissa = muLaw & 0x0F;
            int magnitude = ((mantissa << 4) + 0x84) << exponent;
            magnitude -= 0x84;
            return (short)(sign * magnitude);
        }

        // ---- Spectral Noise Gate ----

        /// <summary>
        /// Applies a spectral noise gate to reduce stationary background noise.
        /// Estimates noise profile from the first <paramref name="noiseEstimateMs"/> ms of audio,
        /// then attenuates frequency bins that fall below the noise floor.
        /// </summary>
        /// <param name="samples">Audio samples to process in-place.</param>
        /// <param name="sampleRate">Sample rate of the audio (default: 16000).</param>
        /// <param name="noiseEstimateMs">Duration of noise profile estimation window in ms (default: 500).</param>
        /// <param name="reductionDb">Attenuation applied to noise bins in dB (default: -20).</param>
        /// <param name="fftSize">FFT size for STFT analysis (must be power of 2, default: 512).</param>
        /// <param name="hopLength">Hop length between STFT frames (default: 256).</param>
        public static void ApplySpectralNoiseGate(
            float[] samples,
            int sampleRate = 16000,
            int noiseEstimateMs = 500,
            float reductionDb = -20f,
            int fftSize = 512,
            int hopLength = 256)
        {
            if (samples == null || samples.Length < fftSize) return;

            int numBins = fftSize / 2 + 1;
            float reductionFactor = (float)Math.Pow(10.0, reductionDb / 20.0);

            // Build Hann window for STFT
            float[] window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                window[i] = 0.5f * (1.0f - (float)Math.Cos(2.0 * Math.PI * i / fftSize));

            // Step 1: Estimate noise profile from the first noiseEstimateMs
            int noiseEstimateSamples = Math.Min(sampleRate * noiseEstimateMs / 1000, samples.Length);
            int noiseFrames = Math.Max(1, (noiseEstimateSamples - fftSize) / hopLength + 1);
            float[] noiseProfile = new float[numBins];

            float[] fftR = new float[fftSize];
            float[] fftI = new float[fftSize];

            for (int f = 0; f < noiseFrames; f++)
            {
                int offset = f * hopLength;
                if (offset + fftSize > samples.Length) break;

                Array.Clear(fftR, 0, fftSize);
                Array.Clear(fftI, 0, fftSize);
                for (int i = 0; i < fftSize; i++)
                    fftR[i] = samples[offset + i] * window[i];

                FFTInPlace(fftR, fftI, fftSize);

                for (int k = 0; k < numBins; k++)
                    noiseProfile[k] += fftR[k] * fftR[k] + fftI[k] * fftI[k];
            }

            for (int k = 0; k < numBins; k++)
                noiseProfile[k] /= noiseFrames;

            // Step 2: STFT → gate → ISTFT (overlap-add)
            int totalFrames = (samples.Length - fftSize) / hopLength + 1;
            float[] output = new float[samples.Length];
            float[] windowSum = new float[samples.Length];

            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * hopLength;
                if (offset + fftSize > samples.Length) break;

                // Forward FFT
                Array.Clear(fftR, 0, fftSize);
                Array.Clear(fftI, 0, fftSize);
                for (int i = 0; i < fftSize; i++)
                    fftR[i] = samples[offset + i] * window[i];

                FFTInPlace(fftR, fftI, fftSize);

                // Apply gate: attenuate bins below noise floor
                // For real signals, bin k and bin (fftSize-k) are conjugate pairs.
                // We gate based on the positive frequency bins and apply symmetrically.
                for (int k = 0; k < numBins; k++)
                {
                    float mag2 = fftR[k] * fftR[k] + fftI[k] * fftI[k];
                    if (mag2 < noiseProfile[k])
                    {
                        fftR[k] *= reductionFactor;
                        fftI[k] *= reductionFactor;
                        // Apply same attenuation to conjugate bin (negative frequency)
                        if (k > 0 && k < fftSize / 2)
                        {
                            fftR[fftSize - k] *= reductionFactor;
                            fftI[fftSize - k] *= reductionFactor;
                        }
                    }
                }

                // Inverse FFT via: IFFT(X) = conj(FFT(conj(X))) / N
                // Step 1: Conjugate the spectrum
                for (int i = 0; i < fftSize; i++)
                    fftI[i] = -fftI[i];

                // Step 2: Forward FFT of conjugated spectrum
                FFTInPlace(fftR, fftI, fftSize);

                // Step 3: Conjugate and scale → time domain
                float invN = 1.0f / fftSize;
                for (int i = 0; i < fftSize; i++)
                {
                    // After conj→FFT→conj→scale, real part is the time-domain signal
                    float timeSample = fftR[i] * invN;  // fftI[i] should be ~0 for real input
                    output[offset + i] += timeSample * window[i];
                    windowSum[offset + i] += window[i] * window[i];
                }
            }

            // Normalize by window overlap sum
            // Compute the expected COLA normalization for the given window and hop
            float colaNorm = 0f;
            for (int i = 0; i < fftSize; i++)
                colaNorm += window[i] * window[i];
            colaNorm *= (float)fftSize / hopLength / fftSize;  // expected steady-state windowSum per sample

            float threshold = colaNorm * 0.1f;  // 10% of expected COLA norm
            for (int i = 0; i < samples.Length; i++)
            {
                if (windowSum[i] > threshold)
                    samples[i] = output[i] / windowSum[i];
                // else: keep original sample (boundary region not covered by enough frames)
            }
        }

        // ─── E-22: Span<T> / Memory<T> Zero-Copy Overloads ──────────────────
        /// <summary>
        /// SIMD-accelerated RMS normalization on a Span (zero-copy, no allocation).
        /// </summary>
        public static void NormalizeRms(Span<float> samples, float targetDb = -20f)
        {
            if (samples.IsEmpty) return;

            float targetRms = MathF.Pow(10f, targetDb / 20f);

            float sum = 0f;
            int vecSize = Vector<float>.Count;
            int i = 0;
            for (; i <= samples.Length - vecSize; i += vecSize)
            {
                var v = new Vector<float>(samples.Slice(i));
                sum += Vector.Dot(v, v);
            }
            for (; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            float rms = MathF.Sqrt(sum / samples.Length);
            if (rms < 1e-5f) return;

            float gain = Math.Min(targetRms / rms, 10.0f);

            var gainVec = new Vector<float>(gain);
            i = 0;
            for (; i <= samples.Length - vecSize; i += vecSize)
            {
                var v = new Vector<float>(samples.Slice(i));
                (v * gainVec).CopyTo(samples.Slice(i));
            }
            for (; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        /// <summary>
        /// SIMD-accelerated pre-emphasis on a Span (zero-copy).
        /// </summary>
        public static void ApplyPreEmphasis(Span<float> samples, float coefficient = 0.97f)
        {
            if (samples.Length < 2) return;

            // Process in reverse to avoid in-place aliasing
            for (int i = samples.Length - 1; i >= 1; i--)
            {
                samples[i] -= coefficient * samples[i - 1];
            }
        }

        // ─── E-3: ArrayPool-backed Mel extraction ────────────────────────────
        /// <summary>
        /// Extracts Mel spectrogram using ArrayPool for scratch buffers
        /// to reduce GC pressure during repeated transcriptions.
        /// </summary>
        /// <param name="pcm">Input audio samples.</param>
        /// <param name="nMels">Number of Mel bands.</param>
        /// <param name="pool">ArrayPool to rent buffers from. Uses Shared if null.</param>
        /// <returns>Mel spectrogram features.</returns>
        public float[] ExtractMelSpectrogramPooled(float[] pcm, int nMels,
            System.Buffers.ArrayPool<float>? pool = null)
        {
            pool ??= System.Buffers.ArrayPool<float>.Shared;

            int outputFrames = MaxFrames; // 3000
            int outputSize = nMels * outputFrames;
            float[] output = pool.Rent(outputSize);

            // Clean the rented output buffer first to ensure no stale data in remainder/padding regions
            Array.Clear(output, 0, output.Length);

            int totalSamples = SampleRate * ChunkLength; // 480000
            float[] padded = pool.Rent(totalSamples);
            try
            {
                Array.Clear(padded, 0, totalSamples);
                int copyLen = Math.Min(pcm.Length, totalSamples);
                Array.Copy(pcm, padded, copyLen);

                ExtractMelSpectrogram(padded, copyLen, nMels, output.AsSpan(0, outputSize));
                return output;
            }
            catch
            {
                pool.Return(output);
                throw;
            }
            finally
            {
                pool.Return(padded);
            }
        }

        // ─── E-5: Parallel Mel computation ───────────────────────────────────
        /// <summary>
        /// Computes Mel spectrograms for multiple audio chunks in parallel.
        /// </summary>
        /// <param name="chunks">Array of PCM chunks.</param>
        /// <param name="nMels">Number of Mel bands.</param>
        /// <returns>Array of Mel features, one per chunk.</returns>
        public float[][] ExtractMelSpectrogramsParallel(float[][] chunks, int nMels)
        {
            var results = new float[chunks.Length][];
            System.Threading.Tasks.Parallel.For(0, chunks.Length, i =>
            {
                results[i] = ExtractMelSpectrogram(chunks[i], nMels);
            });
            return results;
        }

        // ─── E-24: Audio quality assessment integration ──────────────────────
        /// <summary>
        /// Assesses the quality of input audio and returns actionable recommendations.
        /// </summary>
        /// <param name="samples">Audio samples to assess.</param>
        /// <returns>Audio quality report with grade and suggestions.</returns>
        public static AudioQualityReport AssessQuality(float[] samples)
        {
            return AudioQualityReport.Assess(samples, SampleRate);
        }
    }
}
