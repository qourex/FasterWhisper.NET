// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Qourex.FasterWhisper.NET
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWhisperOptions
    {
        public nuint BeamSize;
        public float Patience;
        public float LengthPenalty;
        public float RepetitionPenalty;
        public nuint NoRepeatNgramSize;
        public nuint MaxLength;
        public nuint SamplingTopk;
        public float SamplingTemperature;
        public nuint NumHypotheses;
        public bool ReturnScores;
        public bool ReturnNoSpeechProb;
        public nuint MaxInitialTimestampIndex;
        public bool SuppressBlank;
        public unsafe int* SuppressTokens;
        public nuint NumSuppressTokens;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWhisperSegment
    {
        public nuint NumTokens;
        public unsafe int* Tokens;
        public float Score;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWhisperResult
    {
        public nuint NumSegments;
        public unsafe NativeWhisperSegment* Segments;
        public float NoSpeechProb;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeLanguageProb
    {
        public fixed byte Language[8];
        public float Probability;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeLanguageDetectionResult
    {
        public nuint NumLanguages;
        public NativeLanguageProb* Languages;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWhisperAlignment
    {
        public nuint TokenIndex;
        public nuint FrameIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeWhisperAlignmentResult
    {
        public nuint NumAlignments;
        public NativeWhisperAlignment* Alignments;
        public nuint NumProbs;
        public float* TextTokenProbs;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeMemoryFile
    {
        public byte* Filename;
        public byte* Data;
        public nuint Size;
    }

    internal static unsafe partial class NativeMethods
    {
        private const string LibName = "qourex_fasterwhisper_native";

        [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8, EntryPoint = "load_whisper_model")]
        public static partial IntPtr LoadWhisperModel(
            string modelPath,
            string device,
            string computeType,
            int[]? deviceIndices,
            nuint numDevices,
            int cpuThreads,
            [MarshalAs(UnmanagedType.U1)] bool flashAttention,
            int numReplicas,
            out IntPtr errorMsg);

        [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8, EntryPoint = "load_whisper_model_from_memory")]
        public static partial IntPtr LoadWhisperModelFromMemory(
            NativeMemoryFile* files,
            nuint numFiles,
            string device,
            string computeType,
            int[]? deviceIndices,
            nuint numDevices,
            int cpuThreads,
            [MarshalAs(UnmanagedType.U1)] bool flashAttention,
            int numReplicas,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "free_whisper_model")]
        public static partial void FreeWhisperModel(IntPtr model);

        [LibraryImport(LibName, EntryPoint = "whisper_generate")]
        public static partial NativeWhisperResult* WhisperGenerate(
            IntPtr model,
            float[] melFeatures,
            nuint batchSize,
            nuint nMels,
            nuint nFrames,
            int[]? promptTokens,
            nuint promptLen,
            NativeWhisperOptions options,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "free_whisper_result")]
        public static partial void FreeWhisperResult(NativeWhisperResult* result);

        [LibraryImport(LibName, EntryPoint = "free_string")]
        public static partial void FreeString(IntPtr str);

        [LibraryImport(LibName, EntryPoint = "whisper_is_multilingual")]
        public static partial byte WhisperIsMultilingual(IntPtr model);

        [LibraryImport(LibName, EntryPoint = "whisper_n_mels")]
        public static partial nuint WhisperNMels(IntPtr model);

        [LibraryImport(LibName, EntryPoint = "whisper_num_languages")]
        public static partial nuint WhisperNumLanguages(IntPtr model);

        [LibraryImport(LibName, EntryPoint = "whisper_detect_language")]
        public static partial NativeLanguageDetectionResult* WhisperDetectLanguage(
            IntPtr model,
            float[] melFeatures,
            nuint batchSize,
            nuint nMels,
            nuint nFrames,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "free_language_detection_result")]
        public static partial void FreeLanguageDetectionResult(NativeLanguageDetectionResult* result);

        [LibraryImport(LibName, EntryPoint = "whisper_align")]
        public static partial NativeWhisperAlignmentResult* WhisperAlign(
            IntPtr model,
            float[] melFeatures,
            nuint batchSize,
            nuint nMels,
            nuint nFrames,
            int[]? startSequence,
            nuint startSequenceLen,
            int[] textTokens,
            nuint textTokensLen,
            int medianFilterWidth,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "free_alignment_result")]
        public static partial void FreeAlignmentResult(NativeWhisperAlignmentResult* result);

        // E-4: Encoder output caching APIs
        [LibraryImport(LibName, EntryPoint = "whisper_encode")]
        public static partial IntPtr WhisperEncode(
            IntPtr model,
            float[] melFeatures,
            nuint batchSize,
            nuint nMels,
            nuint nFrames,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "whisper_decode")]
        public static partial NativeWhisperResult* WhisperDecode(
            IntPtr model,
            IntPtr encoderOutput,
            int[]? promptTokens,
            nuint promptLen,
            NativeWhisperOptions options,
            out IntPtr errorMsg);

        [LibraryImport(LibName, EntryPoint = "free_encoder_output")]
        public static partial void FreeEncoderOutput(IntPtr encoderOutput);
    }
}
