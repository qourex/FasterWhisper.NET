// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

#pragma once

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

#include <cstddef>

extern "C" {
    struct NativeWhisperOptions {
        size_t beam_size;
        float patience;
        float length_penalty;
        float repetition_penalty;
        size_t no_repeat_ngram_size;
        size_t max_length;
        size_t sampling_topk;
        float sampling_temperature;
        size_t num_hypotheses;
        bool return_scores;
        bool return_no_speech_prob;
        size_t max_initial_timestamp_index;
        bool suppress_blank;
        const int* suppress_tokens;
        size_t num_suppress_tokens;
    };

    struct NativeWhisperSegment {
        size_t num_tokens;
        int* tokens;
        float score;
    };

    struct NativeWhisperResult {
        size_t num_segments;
        NativeWhisperSegment* segments;
        float no_speech_prob;
    };

    struct NativeLanguageProb {
        char language[8];
        float probability;
    };

    struct NativeLanguageDetectionResult {
        size_t num_languages;
        NativeLanguageProb* languages;
    };

    struct NativeWhisperAlignment {
        size_t token_index;
        size_t frame_index;
    };

    struct NativeWhisperAlignmentResult {
        size_t num_alignments;
        NativeWhisperAlignment* alignments;
        size_t num_probs;
        float* text_token_probs;
    };

    struct NativeMemoryFile {
        const char* filename;
        const unsigned char* data;
        size_t size;
    };

    /// @brief Load the Whisper model from a path on disk.
    /// @param model_path Path to the directory containing model weights (model.bin, config.json, etc.).
    /// @param device Device to load the model on ("cpu" or "cuda").
    /// @param compute_type Quantization and precision compute type (e.g., "float16", "int8").
    /// @param device_indices Array of device IDs/indices to allocate replicas on (e.g., [0]).
    /// @param num_devices Number of elements in the device_indices array.
    /// @param cpu_threads Number of threads to allocate per replica for CPU execution.
    /// @param flash_attention If true, enables flash attention optimization (CUDA >= 8.0).
    /// @param num_replicas Number of model replicas to load for concurrent inference.
    /// @param error_msg Output pointer receiving C-style error message string on failure. Must be freed using free_string().
    /// @return Opaque pointer to the loaded Whisper model instance. Must be freed using free_whisper_model().
    EXPORT void* load_whisper_model(const char* model_path, const char* device, const char* compute_type, const int* device_indices, size_t num_devices, int cpu_threads, bool flash_attention, int num_replicas, char** error_msg);

    /// @brief Load the Whisper model from in-memory byte arrays.
    /// @param files Array of NativeMemoryFile structures containing in-memory files.
    /// @param num_files Number of files in the files array.
    /// @param device Device to load the model on ("cpu" or "cuda").
    /// @param compute_type Quantization and precision compute type (e.g., "float16", "int8").
    /// @param device_indices Array of device IDs/indices to allocate replicas on (e.g., [0]).
    /// @param num_devices Number of elements in the device_indices array.
    /// @param cpu_threads Number of threads to allocate per replica for CPU execution.
    /// @param flash_attention If true, enables flash attention optimization (CUDA >= 8.0).
    /// @param num_replicas Number of model replicas to load for concurrent inference.
    /// @param error_msg Output pointer receiving C-style error message string on failure. Must be freed using free_string().
    /// @return Opaque pointer to the loaded Whisper model instance. Must be freed using free_whisper_model().
    EXPORT void* load_whisper_model_from_memory(const NativeMemoryFile* files, size_t num_files, const char* device, const char* compute_type, const int* device_indices, size_t num_devices, int cpu_threads, bool flash_attention, int num_replicas, char** error_msg);

    /// @brief Free the Whisper model instance.
    /// @param model Opaque pointer to the loaded model.
    EXPORT void free_whisper_model(void* model);

    /// @brief Transcribe a batch of Mel spectrogram features.
    /// @param model Opaque pointer to the model.
    /// @param mel_features Array of Log-Mel Spectrogram features.
    /// @param batch_size Number of Mel features to transcribe concurrently.
    /// @param n_mels Number of Mel frequency bands (80 or 128).
    /// @param n_frames Number of frames in the spectrogram (typically 3000 for 30s).
    /// @param prompt_tokens Optional prompt token IDs to condition transcription.
    /// @param prompt_len Number of prompt tokens.
    /// @param options Native configuration options for transcription.
    /// @param error_msg Output error message pointer. Must be freed with free_string().
    /// @return Pointer to NativeWhisperResult. Must be freed using free_whisper_result().
    EXPORT NativeWhisperResult* whisper_generate(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, const int* prompt_tokens, size_t prompt_len, NativeWhisperOptions options, char** error_msg);

    /// @brief Free the NativeWhisperResult structure and all nested segments/tokens.
    /// @param result Pointer to result structure to free.
    EXPORT void free_whisper_result(NativeWhisperResult* result);

    /// @brief Free a C-style string allocated by the native code.
    /// @param str Pointer to string to free.
    EXPORT void free_string(char* str);

    /// @brief Check if the model is multilingual.
    /// @param model Opaque pointer to the model.
    EXPORT bool whisper_is_multilingual(void* model);

    /// @brief Get the number of Mel bands required by the model (80 or 128).
    /// @param model Opaque pointer to the model.
    EXPORT size_t whisper_n_mels(void* model);

    /// @brief Get the number of languages supported by the model.
    /// @param model Opaque pointer to the model.
    EXPORT size_t whisper_num_languages(void* model);

    /// @brief Detect languages in the Mel spectrogram.
    /// @param model Opaque pointer to the model.
    /// @param mel_features Mel spectrogram features.
    /// @param batch_size Batch size.
    /// @param n_mels Number of Mel bands.
    /// @param n_frames Number of frames.
    /// @param error_msg Output error message pointer. Must be freed with free_string().
    /// @return Pointer to detection result. Must be freed using free_language_detection_result().
    EXPORT NativeLanguageDetectionResult* whisper_detect_language(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, char** error_msg);

    /// @brief Free the language detection result.
    /// @param result Pointer to result to free.
    EXPORT void free_language_detection_result(NativeLanguageDetectionResult* result);

    /// @brief Align transcription tokens with audio frames.
    /// @param model Opaque pointer to the model.
    /// @param mel_features Mel spectrogram features.
    /// @param batch_size Batch size.
    /// @param n_mels Number of Mel bands.
    /// @param n_frames Number of frames.
    /// @param start_sequence Start token sequence.
    /// @param start_sequence_len Length of start sequence.
    /// @param text_tokens Token IDs to align.
    /// @param text_tokens_len Length of text tokens.
    /// @param median_filter_width Median filter width for alignment smoothing.
    /// @param error_msg Output error message pointer. Must be freed with free_string().
    /// @return Pointer to alignment result. Must be freed using free_alignment_result().
    EXPORT NativeWhisperAlignmentResult* whisper_align(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, const int* start_sequence, size_t start_sequence_len, const int* text_tokens, size_t text_tokens_len, int median_filter_width, char** error_msg);

    /// @brief Free the alignment result.
    /// @param result Pointer to result to free.
    EXPORT void free_alignment_result(NativeWhisperAlignmentResult* result);

    /// @brief Encode Mel spectrogram features to get encoder output (for caching).
    /// @param model Opaque pointer to the model.
    /// @param mel_features Mel spectrogram features.
    /// @param batch_size Batch size.
    /// @param n_mels Number of Mel bands.
    /// @param n_frames Number of frames.
    /// @param error_msg Output error message pointer. Must be freed with free_string().
    /// @return Opaque pointer to the encoder output. Must be freed using free_encoder_output().
    EXPORT void* whisper_encode(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, char** error_msg);

    /// @brief Decode tokens using cached encoder output.
    /// @param model Opaque pointer to the model.
    /// @param encoder_output Opaque pointer to the cached encoder output.
    /// @param prompt_tokens Optional prompt token IDs.
    /// @param prompt_len Length of prompt tokens.
    /// @param options Native configuration options.
    /// @param error_msg Output error message pointer. Must be freed with free_string().
    /// @return Pointer to NativeWhisperResult. Must be freed using free_whisper_result().
    EXPORT NativeWhisperResult* whisper_decode(void* model, void* encoder_output, const int* prompt_tokens, size_t prompt_len, NativeWhisperOptions options, char** error_msg);

    /// @brief Free the cached encoder output.
    /// @param encoder_output Opaque pointer to encoder output.
    EXPORT void free_encoder_output(void* encoder_output);
}
