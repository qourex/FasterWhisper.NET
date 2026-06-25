// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Downloads CTranslate2-converted Whisper models from Hugging Face Hub.
    /// </summary>
    public class ModelDownloader
    {
        private static readonly Dictionary<string, string> ModelMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            { "tiny", "Systran/faster-whisper-tiny" },
            { "tiny.en", "Systran/faster-whisper-tiny.en" },
            { "base", "Systran/faster-whisper-base" },
            { "base.en", "Systran/faster-whisper-base.en" },
            { "small", "Systran/faster-whisper-small" },
            { "small.en", "Systran/faster-whisper-small.en" },
            { "medium", "Systran/faster-whisper-medium" },
            { "medium.en", "Systran/faster-whisper-medium.en" },
            { "large-v1", "Systran/faster-whisper-large-v1" },
            { "large-v2", "Systran/faster-whisper-large-v2" },
            { "large-v3", "Systran/faster-whisper-large-v3" },
            { "large-v3-turbo", "Systran/faster-whisper-large-v3-turbo" },
            { "faster-distil-whisper-large-v3", "Systran/faster-distil-whisper-large-v3" }
        };

        private static readonly Dictionary<string, string> ModelHashes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "tiny", "DCB76C6586FC06CBDAC6DD21F14CFD129CC4CDD9DCE19BF4FFA62E59CBE6E6D1" },
            { "tiny.en", "1A5AFAE06A4DB91C975C9A9D78be5cc110ee4ea022ad57d55492e4550e936b2a" },
            { "base", "D01C3014881C9C6F3133C182F3D2887EB6CA1C789A7538C5C007196857A0A6A9" },
            { "base.en", "2A166925539A16005F14FF328359f9b9adb9dc4fb631bb3b227526862e93e2ef" },
            { "small", "3E305921506D8872816023E4C273E75D2419fb89b24da97b4fe7bce14170d671" },
            { "small.en", "62B2A45B05EE59ACB4A5341B33EE35E041395D378D418A18ACFE4C9E768EE37A" },
            { "medium", "9B45E1009DCC4AB601EFF815b61d80e60ce3fd8c74c1a14f4a282258286b51ae" },
            { "medium.en", "11B220779AEA4C6F3CE9D2549C8A95EA869ED84066864b999531EF53E594FE5B" },
            { "large-v1", "A3CCE8081A5414206AB09A80AA410EBF9965FEEF52ADAFEAEAD13F4A83398B1D1" },
            { "large-v2", "BF2A9746382E1AA7FFFF6B3A0D137ED9EDBD9670C3B87E5D35F5E85E70D0333A" },
            { "large-v3", "69F74147E3334731BC3A76048724833325d2ec74642fb52620eda87352e3d4f1" },
            { "faster-distil-whisper-large-v3", "B79368E19B6623813609431A6E5EE309A71506701EBC49FD7820E692DEC7C5F5" }
        };

        private static readonly string[] RequiredFiles =
        {
            "model.bin",
            "config.json"
        };

        private static readonly string[] OptionalFiles =
        {
            "preprocessor_config.json",
            "tokenizer.json"
        };

        private static readonly HttpClient SharedHttpClient = new();
        private readonly HttpClient _httpClient;
        private readonly string _cacheDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelDownloader"/> class.
        /// </summary>
        /// <param name="cacheDirectory">Optional custom cache directory. Defaults to ~/.cache/qourex-fasterwhisper.</param>
        /// <param name="httpClient">Optional HttpClient to use for downloading.</param>
        public ModelDownloader(string? cacheDirectory = null, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
            _cacheDirectory = cacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "qourex-fasterwhisper"
            );
        }

        /// <summary>
        /// Resolves a model shorthand to its Hugging Face repository ID.
        /// If the name is not a known shorthand, returns the input unchanged (assumed to be a repo ID or path).
        /// </summary>
        /// <param name="modelNameOrPath">Model shorthand (e.g. "tiny", "large-v3") or custom repo ID.</param>
        /// <returns>The resolved Hugging Face repository ID.</returns>
        public static string ResolveRepoId(string modelNameOrPath)
        {
            if (ModelMapping.TryGetValue(modelNameOrPath, out string? repoId))
                return repoId;
            return modelNameOrPath;
        }

        /// <summary>
        /// Gets the expected local cache directory path for a given model name.
        /// </summary>
        /// <param name="modelNameOrPath">Model shorthand or repo ID.</param>
        /// <param name="cacheDirectory">Optional cache root override.</param>
        /// <returns>The full path to the model cache directory.</returns>
        public static string GetModelDirectory(string modelNameOrPath, string? cacheDirectory = null)
        {
            string cacheRoot = cacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "qourex-fasterwhisper"
            );
            string folderName = ModelMapping.ContainsKey(modelNameOrPath)
                ? modelNameOrPath.ToLowerInvariant()
                : modelNameOrPath.Replace('/', '_');
            return Path.Combine(cacheRoot, folderName);
        }

        /// <summary>
        /// Resolves the model path, downloading it if it is a shorthand name and not yet present locally.
        /// </summary>
        /// <param name="modelNameOrPath">A Hugging Face repo ID, a model shorthand (e.g., "tiny", "large-v3"), or a local directory path.</param>
        /// <param name="progress">Progress callback reporting filename, bytes downloaded, and total bytes.</param>
        /// <param name="cancellationToken">Token to cancel the download operation.</param>
        /// <returns>The local directory path containing the model files.</returns>
        public async Task<string> GetModelPathAsync(
            string modelNameOrPath,
            IProgress<(string FileName, long BytesRead, long TotalBytes)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // 1. If it's an existing directory containing model.bin, return it immediately
            if (Directory.Exists(modelNameOrPath) && File.Exists(Path.Combine(modelNameOrPath, "model.bin")))
            {
                return Path.GetFullPath(modelNameOrPath);
            }

            // 2. Resolve HuggingFace repository ID
            string repoId = modelNameOrPath;
            string folderName = modelNameOrPath.Replace('/', '_');
            if (ModelMapping.TryGetValue(modelNameOrPath, out string? mappedRepoId))
            {
                repoId = mappedRepoId;
                folderName = modelNameOrPath.ToLowerInvariant();
            }
            else if (!repoId.Contains('/'))
            {
                throw new ArgumentException($"Invalid model name or local directory: '{modelNameOrPath}'. Must be a valid shorthand (e.g., 'base', 'large-v3') or a Hugging Face repository ID (e.g., 'Systran/faster-whisper-tiny').", nameof(modelNameOrPath));
            }

            string targetDir = Path.Combine(_cacheDirectory, folderName);
            Directory.CreateDirectory(targetDir);

            // 3. Download required files
            foreach (string file in RequiredFiles)
            {
                string localFilePath = Path.Combine(targetDir, file);
                if (File.Exists(localFilePath))
                {
                    continue;
                }

                string url = $"https://huggingface.co/{repoId}/resolve/main/{file}";
                await DownloadFileWithProgressAsync(url, localFilePath, progress, cancellationToken).ConfigureAwait(false);
            }

            // 3.5. Ensure we have either vocabulary.txt or vocabulary.json
            string vocabTxtPath = Path.Combine(targetDir, "vocabulary.txt");
            string vocabJsonPath = Path.Combine(targetDir, "vocabulary.json");

            if (!File.Exists(vocabTxtPath) && !File.Exists(vocabJsonPath))
            {
                // Try downloading vocabulary.txt first
                string vocabTxtUrl = $"https://huggingface.co/{repoId}/resolve/main/vocabulary.txt";
                try
                {
                    await DownloadFileWithProgressAsync(vocabTxtUrl, vocabTxtPath, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // If vocabulary.txt fails, try vocabulary.json
                    string vocabJsonUrl = $"https://huggingface.co/{repoId}/resolve/main/vocabulary.json";
                    try
                    {
                        await DownloadFileWithProgressAsync(vocabJsonUrl, vocabJsonPath, progress, cancellationToken).ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new FileNotFoundException($"Could not find or download either 'vocabulary.txt' or 'vocabulary.json' from Hugging Face repository '{repoId}'. Ensure the repository contains a valid vocabulary file.", ex);
                    }
                }
            }

            // 4. Download optional files (ignore 404s)
            foreach (string file in OptionalFiles)
            {
                string localFilePath = Path.Combine(targetDir, file);
                if (File.Exists(localFilePath))
                {
                    continue;
                }

                string url = $"https://huggingface.co/{repoId}/resolve/main/{file}";
                try
                {
                    await DownloadFileWithProgressAsync(url, localFilePath, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Optional file not found in the repo, skip silently
                }
            }

            // 5. Verify integrity of model.bin if it is an official model with a known hash
            string modelBinPath = Path.Combine(targetDir, "model.bin");
            if (File.Exists(modelBinPath) && ModelHashes.TryGetValue(folderName, out string? expectedHash))
            {
                string actualHash = CalculateSha256(modelBinPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(modelBinPath); } catch { /* Ignore delete errors */ }
                    throw new System.Security.Cryptography.CryptographicException($"Integrity check failed for model file '{modelBinPath}'. Expected SHA256: {expectedHash}, Actual: {actualHash}. File has been deleted.");
                }
            }

            return targetDir;
        }

        private static string CalculateSha256(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = sha256.ComputeHash(stream);
            // Convert to hex string
            var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        private async Task DownloadFileWithProgressAsync(
            string url,
            string destinationPath,
            IProgress<(string FileName, long BytesRead, long TotalBytes)>? progress,
            CancellationToken cancellationToken)
        {
            string fileName = Path.GetFileName(destinationPath);
            string tempPath = destinationPath + ".tmp";

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                byte[] buffer = new byte[8192];
                long totalBytesRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                    totalBytesRead += bytesRead;

                    progress?.Report((fileName, totalBytesRead, totalBytes));
                }

                fileStream.Close();
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                File.Move(tempPath, destinationPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* Ignore */ }
                }
            }
        }
    }
}
