// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

#include "qourex_fasterwhisper_native.h"
#include "ctranslate2/models/whisper.h"
#include "ctranslate2/storage_view.h"
#include "ctranslate2/devices.h"
#include "ctranslate2/types.h"
#include <cstring>
#include <vector>
#include <string>
#include <algorithm>

#ifdef _WIN32
#define STRDUP _strdup
#else
#define STRDUP strdup
#endif

#include <sstream>
#include <unordered_map>
#include "ctranslate2/models/model_reader.h"

class MemoryModelReader : public ctranslate2::models::ModelReader {
private:
    std::string _model_id;
    std::unordered_map<std::string, std::pair<const unsigned char*, size_t>> _files;

public:
    MemoryModelReader(const std::string& model_id, const NativeMemoryFile* files, size_t num_files)
        : _model_id(model_id) {
        for (size_t i = 0; i < num_files; ++i) {
            _files[files[i].filename] = {files[i].data, files[i].size};
        }
    }

    std::string get_model_id() const override {
        return _model_id;
    }

    std::unique_ptr<std::istream> get_file(const std::string& filename, const bool binary) override {
        auto it = _files.find(filename);
        if (it == _files.end()) {
            return nullptr;
        }

        struct MemoryBuffer : std::streambuf {
            MemoryBuffer(const unsigned char* begin, size_t size) {
                this->setg((char*)begin, (char*)begin, (char*)begin + size);
            }
        };

        class MemoryIStream : public std::istream {
        private:
            MemoryBuffer _buf;
        public:
            MemoryIStream(const unsigned char* begin, size_t size)
                : std::istream(nullptr), _buf(begin, size) {
                this->init(&_buf);
            }
        };

        return std::make_unique<MemoryIStream>(it->second.first, it->second.second);
    }
};

extern "C" {
    void* load_whisper_model(const char* model_path, const char* device, const char* compute_type, const int* device_indices, size_t num_devices, int cpu_threads, bool flash_attention, int num_replicas, char** error_msg) {
        if (error_msg) *error_msg = nullptr;
        try {
            ctranslate2::Device dev = ctranslate2::str_to_device(device ? device : "cpu");
            ctranslate2::ComputeType ct = ctranslate2::str_to_compute_type(compute_type ? compute_type : "default");
            
            std::vector<int> indices;
            if (device_indices && num_devices > 0) {
                for (size_t i = 0; i < num_devices; ++i) {
                    indices.push_back(device_indices[i]);
                }
            } else {
                indices = {0};
            }

            ctranslate2::ReplicaPoolConfig pool_config;
            if (cpu_threads > 0) {
                pool_config.num_threads_per_replica = cpu_threads;
            }

            ctranslate2::models::ModelLoader model_loader(model_path);
            model_loader.device = dev;
            model_loader.compute_type = ct;
            model_loader.device_indices = indices;
            model_loader.use_flash_attention = flash_attention;
            if (num_replicas > 0) {
                model_loader.num_replicas_per_device = num_replicas;
            }

            auto* whisper = new ctranslate2::models::Whisper(model_loader, pool_config);

            return whisper;
        } catch (const std::exception& e) {

            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    void* load_whisper_model_from_memory(const NativeMemoryFile* files, size_t num_files, const char* device, const char* compute_type, const int* device_indices, size_t num_devices, int cpu_threads, bool flash_attention, int num_replicas, char** error_msg) {
        if (error_msg) *error_msg = nullptr;
        if (!files || num_files == 0) {

            if (error_msg) *error_msg = STRDUP("No memory files provided");
            return nullptr;
        }
        try {
            ctranslate2::Device dev = ctranslate2::str_to_device(device ? device : "cpu");
            ctranslate2::ComputeType ct = ctranslate2::str_to_compute_type(compute_type ? compute_type : "default");
            
            std::vector<int> indices;
            if (device_indices && num_devices > 0) {
                for (size_t i = 0; i < num_devices; ++i) {
                    indices.push_back(device_indices[i]);
                }
            } else {
                indices = {0};
            }

            ctranslate2::ReplicaPoolConfig pool_config;
            if (cpu_threads > 0) {
                pool_config.num_threads_per_replica = cpu_threads;
            }

            auto reader = std::make_shared<MemoryModelReader>("memory_model", files, num_files);
            ctranslate2::models::ModelLoader model_loader(reader);
            model_loader.device = dev;
            model_loader.compute_type = ct;
            model_loader.device_indices = indices;
            model_loader.use_flash_attention = flash_attention;
            if (num_replicas > 0) {
                model_loader.num_replicas_per_device = num_replicas;
            }

            auto* whisper = new ctranslate2::models::Whisper(model_loader, pool_config);

            return whisper;
        } catch (const std::exception& e) {

            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }


    void free_whisper_model(void* model) {
        try {
            if (model) {
                delete static_cast<ctranslate2::models::Whisper*>(model);
            }
        } catch (...) {
            // Swallow exceptions to prevent them from escaping into managed code during cleanup
        }
    }

    bool whisper_is_multilingual(void* model) {
        if (!model) return false;
        return static_cast<ctranslate2::models::Whisper*>(model)->is_multilingual();
    }

    size_t whisper_n_mels(void* model) {
        if (!model) return 0;
        return static_cast<ctranslate2::models::Whisper*>(model)->n_mels();
    }

    size_t whisper_num_languages(void* model) {
        if (!model) return 0;
        return static_cast<ctranslate2::models::Whisper*>(model)->num_languages();
    }

    NativeWhisperResult* whisper_generate(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, const int* prompt_tokens, size_t prompt_len, NativeWhisperOptions options, char** error_msg) {
        if (error_msg) *error_msg = nullptr;

        if (!model || !mel_features) {
            if (error_msg) *error_msg = STRDUP("Model or mel_features is null");
            return nullptr;
        }

        try {
            auto* whisper = static_cast<ctranslate2::models::Whisper*>(model);

            ctranslate2::Shape shape = { static_cast<ctranslate2::dim_t>(batch_size), static_cast<ctranslate2::dim_t>(n_mels), static_cast<ctranslate2::dim_t>(n_frames) };
            std::vector<float> features_vec(mel_features, mel_features + batch_size * n_mels * n_frames);
            ctranslate2::StorageView features(shape, features_vec, ctranslate2::Device::CPU);

            std::vector<std::vector<size_t>> prompts(batch_size);
            if (prompt_tokens && prompt_len > 0) {
                std::vector<size_t> prompt(prompt_len);
                for (size_t i = 0; i < prompt_len; ++i) {
                    prompt[i] = static_cast<size_t>(prompt_tokens[i]);
                }
                for (size_t b = 0; b < batch_size; ++b) {
                    prompts[b] = prompt;
                }
            }

            ctranslate2::models::WhisperOptions whisper_options;
            whisper_options.beam_size = options.beam_size;
            whisper_options.patience = options.patience;
            whisper_options.length_penalty = options.length_penalty;
            whisper_options.repetition_penalty = options.repetition_penalty;
            whisper_options.no_repeat_ngram_size = options.no_repeat_ngram_size;
            whisper_options.max_length = options.max_length;
            whisper_options.sampling_topk = options.sampling_topk;
            whisper_options.sampling_temperature = options.sampling_temperature;
            whisper_options.num_hypotheses = options.num_hypotheses;
            whisper_options.return_scores = options.return_scores;
            whisper_options.return_no_speech_prob = options.return_no_speech_prob;
            whisper_options.max_initial_timestamp_index = options.max_initial_timestamp_index;
            whisper_options.suppress_blank = options.suppress_blank;
            
            if (options.suppress_tokens && options.num_suppress_tokens > 0) {
                std::vector<int> suppress(options.num_suppress_tokens);
                for (size_t i = 0; i < options.num_suppress_tokens; ++i) {
                    suppress[i] = options.suppress_tokens[i];
                }
                whisper_options.suppress_tokens = suppress;
            }

            auto futures = whisper->generate(features, prompts, whisper_options);
            
            // Retrieve all results first. If any .get() throws, no raw pointers have been allocated yet.
            std::vector<ctranslate2::models::WhisperGenerationResult> gen_results;
            gen_results.reserve(futures.size());
            for (auto& fut : futures) {
                gen_results.push_back(fut.get());
            }

            auto* result = new NativeWhisperResult();
            result->num_segments = gen_results.size();
            result->segments = nullptr;
            result->no_speech_prob = 0.0f;

            try {
                result->segments = new NativeWhisperSegment[gen_results.size()];
                for (size_t i = 0; i < gen_results.size(); ++i) {
                    result->segments[i].num_tokens = 0;
                    result->segments[i].tokens = nullptr;
                    result->segments[i].score = 0.0f;
                }

                for (size_t i = 0; i < gen_results.size(); ++i) {
                    auto& gen_res = gen_results[i];
                    result->no_speech_prob = gen_res.no_speech_prob;

                    if (!gen_res.sequences_ids.empty()) {
                        auto& ids = gen_res.sequences_ids[0];
                        result->segments[i].num_tokens = ids.size();
                        int* tokens_arr = new int[ids.size()];
                        for (size_t t = 0; t < ids.size(); ++t) {
                            tokens_arr[t] = static_cast<int>(ids[t]);
                        }
                        result->segments[i].tokens = tokens_arr;
                        result->segments[i].score = gen_res.scores.empty() ? 0.0f : gen_res.scores[0];
                    }
                }
            } catch (...) {
                free_whisper_result(result);
                throw;
            }

            return result;
        } catch (const std::exception& e) {
            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    void free_whisper_result(NativeWhisperResult* result) {
        try {
            if (result) {
                if (result->segments) {
                    for (size_t i = 0; i < result->num_segments; ++i) {
                        if (result->segments[i].tokens) {
                            delete[] result->segments[i].tokens;
                        }
                    }
                    delete[] result->segments;
                }
                delete result;
            }
        } catch (...) {
            // Swallow exceptions to prevent them from escaping into managed code during cleanup
        }
    }

    void free_string(char* str) {
        try {
            if (str) {
                free(str);
            }
        } catch (...) {
            // Swallow exceptions to prevent them from escaping into managed code during cleanup
        }
    }

    NativeLanguageDetectionResult* whisper_detect_language(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, char** error_msg) {
        if (error_msg) *error_msg = nullptr;
        if (!model || !mel_features) {
            if (error_msg) *error_msg = STRDUP("Model or mel_features is null");
            return nullptr;
        }

        try {
            auto* whisper = static_cast<ctranslate2::models::Whisper*>(model);

            ctranslate2::Shape shape = { static_cast<ctranslate2::dim_t>(batch_size), static_cast<ctranslate2::dim_t>(n_mels), static_cast<ctranslate2::dim_t>(n_frames) };
            std::vector<float> features_vec(mel_features, mel_features + batch_size * n_mels * n_frames);
            ctranslate2::StorageView features(shape, features_vec, ctranslate2::Device::CPU);

            auto futures = whisper->detect_language(features);
            if (futures.empty()) {
                if (error_msg) *error_msg = STRDUP("Language detection returned no results");
                return nullptr;
            }

            auto results = futures[0].get();

            auto* res = new NativeLanguageDetectionResult();
            res->num_languages = results.size();
            res->languages = nullptr;

            try {
                res->languages = new NativeLanguageProb[results.size()];
                for (size_t i = 0; i < results.size(); ++i) {
                    std::strncpy(res->languages[i].language, results[i].first.c_str(), sizeof(res->languages[i].language) - 1);
                    res->languages[i].language[sizeof(res->languages[i].language) - 1] = '\0';
                    res->languages[i].probability = results[i].second;
                }
            } catch (...) {
                free_language_detection_result(res);
                throw;
            }

            return res;
        } catch (const std::exception& e) {
            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    void free_language_detection_result(NativeLanguageDetectionResult* result) {
        try {
            if (result) {
                if (result->languages) {
                    delete[] result->languages;
                }
                delete result;
            }
        } catch (...) {
            // Swallow exceptions to prevent them from escaping into managed code during cleanup
        }
    }

    NativeWhisperAlignmentResult* whisper_align(void* model, const float* mel_features, size_t batch_size, size_t n_mels, size_t n_frames, const int* start_sequence, size_t start_sequence_len, const int* text_tokens, size_t text_tokens_len, int median_filter_width, char** error_msg) {
        if (error_msg) *error_msg = nullptr;
        if (!model || !mel_features) {
            if (error_msg) *error_msg = STRDUP("Model or mel_features is null");
            return nullptr;
        }

        try {
            auto* whisper = static_cast<ctranslate2::models::Whisper*>(model);

            ctranslate2::Shape shape = { static_cast<ctranslate2::dim_t>(batch_size), static_cast<ctranslate2::dim_t>(n_mels), static_cast<ctranslate2::dim_t>(n_frames) };
            std::vector<float> features_vec(mel_features, mel_features + batch_size * n_mels * n_frames);
            ctranslate2::StorageView features(shape, features_vec, ctranslate2::Device::CPU);

            std::vector<size_t> start_seq;
            if (start_sequence && start_sequence_len > 0) {
                start_seq.resize(start_sequence_len);
                for (size_t i = 0; i < start_sequence_len; ++i) {
                    start_seq[i] = static_cast<size_t>(start_sequence[i]);
                }
            }

            std::vector<std::vector<size_t>> tokens(batch_size);
            if (text_tokens && text_tokens_len > 0) {
                std::vector<size_t> t(text_tokens_len);
                for (size_t i = 0; i < text_tokens_len; ++i) {
                    t[i] = static_cast<size_t>(text_tokens[i]);
                }
                for (size_t b = 0; b < batch_size; ++b) {
                    tokens[b] = t;
                }
            }

            std::vector<size_t> frames(batch_size, n_frames);

            auto futures = whisper->align(features, start_seq, tokens, frames, median_filter_width);
            if (futures.empty()) {
                if (error_msg) *error_msg = STRDUP("Alignment returned no results");
                return nullptr;
            }

            auto align_res = futures[0].get();

            auto* res = new NativeWhisperAlignmentResult();
            res->num_alignments = align_res.alignments.size();
            res->alignments = nullptr;
            res->num_probs = align_res.text_token_probs.size();
            res->text_token_probs = nullptr;

            try {
                res->alignments = new NativeWhisperAlignment[align_res.alignments.size()];
                for (size_t i = 0; i < align_res.alignments.size(); ++i) {
                    res->alignments[i].token_index = align_res.alignments[i].first;
                    res->alignments[i].frame_index = align_res.alignments[i].second;
                }

                res->text_token_probs = new float[align_res.text_token_probs.size()];
                for (size_t i = 0; i < align_res.text_token_probs.size(); ++i) {
                    res->text_token_probs[i] = align_res.text_token_probs[i];
                }
            } catch (...) {
                free_alignment_result(res);
                throw;
            }

            return res;
        } catch (const std::exception& e) {
            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    void free_alignment_result(NativeWhisperAlignmentResult* result) {
        try {
            if (result) {
                if (result->alignments) {
                    delete[] result->alignments;
                }
                if (result->text_token_probs) {
                    delete[] result->text_token_probs;
                }
                delete result;
            }
        } catch (...) {
            // Swallow exceptions to prevent them from escaping into managed code during cleanup
        }
    }

    // E-4: Encoder output caching — split encode and decode steps
    // Encode Mel features → encoder output (run once per chunk)
    void* whisper_encode(void* model, const float* mel_features, size_t batch_size,
                         size_t n_mels, size_t n_frames, char** error_msg) {
        if (error_msg) *error_msg = nullptr;

        if (!model || !mel_features) {
            if (error_msg) *error_msg = STRDUP("Model or mel_features is null");
            return nullptr;
        }

        try {
            auto* whisper = static_cast<ctranslate2::models::Whisper*>(model);

            ctranslate2::Shape shape = { static_cast<ctranslate2::dim_t>(batch_size),
                                         static_cast<ctranslate2::dim_t>(n_mels),
                                         static_cast<ctranslate2::dim_t>(n_frames) };
            std::vector<float> features_vec(mel_features, mel_features + batch_size * n_mels * n_frames);
            ctranslate2::StorageView features(shape, features_vec, ctranslate2::Device::CPU);

            auto future = whisper->encode(features, false);
            auto* encoder_output = new ctranslate2::StorageView(future.get());
            return static_cast<void*>(encoder_output);
        } catch (const std::exception& e) {
            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    // Decode from cached encoder output (run per temperature retry)
    NativeWhisperResult* whisper_decode(void* model, void* encoder_output,
                                         const int* prompt_tokens, size_t prompt_len,
                                         NativeWhisperOptions options, char** error_msg) {
        if (error_msg) *error_msg = nullptr;

        if (!model || !encoder_output) {
            if (error_msg) *error_msg = STRDUP("Model or encoder_output is null");
            return nullptr;
        }

        try {
            auto* whisper = static_cast<ctranslate2::models::Whisper*>(model);
            auto* enc_out = static_cast<ctranslate2::StorageView*>(encoder_output);

            size_t batch_size = enc_out->dim(0);

            std::vector<std::vector<size_t>> prompts(batch_size);
            if (prompt_tokens && prompt_len > 0) {
                std::vector<size_t> prompt(prompt_len);
                for (size_t i = 0; i < prompt_len; ++i) {
                    prompt[i] = static_cast<size_t>(prompt_tokens[i]);
                }
                for (size_t b = 0; b < batch_size; ++b) {
                    prompts[b] = prompt;
                }
            }

            ctranslate2::models::WhisperOptions whisper_options;
            whisper_options.beam_size = options.beam_size;
            whisper_options.patience = options.patience;
            whisper_options.length_penalty = options.length_penalty;
            whisper_options.repetition_penalty = options.repetition_penalty;
            whisper_options.no_repeat_ngram_size = options.no_repeat_ngram_size;
            whisper_options.max_length = options.max_length;
            whisper_options.sampling_topk = options.sampling_topk;
            whisper_options.sampling_temperature = options.sampling_temperature;
            whisper_options.num_hypotheses = options.num_hypotheses;
            whisper_options.return_scores = options.return_scores;
            whisper_options.return_no_speech_prob = options.return_no_speech_prob;
            whisper_options.max_initial_timestamp_index = options.max_initial_timestamp_index;
            whisper_options.suppress_blank = options.suppress_blank;

            if (options.suppress_tokens && options.num_suppress_tokens > 0) {
                std::vector<int> suppress(options.num_suppress_tokens);
                for (size_t i = 0; i < options.num_suppress_tokens; ++i) {
                    suppress[i] = options.suppress_tokens[i];
                }
                whisper_options.suppress_tokens = suppress;
            }

            auto futures = whisper->generate(*enc_out, prompts, whisper_options);

            // Retrieve all results first. If any .get() throws, no raw pointers have been allocated yet.
            std::vector<ctranslate2::models::WhisperGenerationResult> gen_results;
            gen_results.reserve(futures.size());
            for (auto& fut : futures) {
                gen_results.push_back(fut.get());
            }

            auto* result = new NativeWhisperResult();
            result->num_segments = gen_results.size();
            result->segments = nullptr;
            result->no_speech_prob = 0.0f;

            try {
                result->segments = new NativeWhisperSegment[gen_results.size()];
                for (size_t i = 0; i < gen_results.size(); ++i) {
                    result->segments[i].num_tokens = 0;
                    result->segments[i].tokens = nullptr;
                    result->segments[i].score = 0.0f;
                }

                for (size_t i = 0; i < gen_results.size(); ++i) {
                    auto& gen_res = gen_results[i];
                    result->no_speech_prob = gen_res.no_speech_prob;

                    if (!gen_res.sequences_ids.empty()) {
                        auto& ids = gen_res.sequences_ids[0];
                        result->segments[i].num_tokens = ids.size();
                        int* tokens_arr = new int[ids.size()];
                        for (size_t t = 0; t < ids.size(); ++t) {
                            tokens_arr[t] = static_cast<int>(ids[t]);
                        }
                        result->segments[i].tokens = tokens_arr;
                        result->segments[i].score = gen_res.scores.empty() ? 0.0f : gen_res.scores[0];
                    }
                }
            } catch (...) {
                free_whisper_result(result);
                throw;
            }

            return result;
        } catch (const std::exception& e) {
            if (error_msg) {
                *error_msg = STRDUP(e.what());
            }
            return nullptr;
        }
    }

    void free_encoder_output(void* encoder_output) {
        try {
            if (encoder_output) {
                delete static_cast<ctranslate2::StorageView*>(encoder_output);
            }
        } catch (...) {
            // Swallow
        }
    }
}
