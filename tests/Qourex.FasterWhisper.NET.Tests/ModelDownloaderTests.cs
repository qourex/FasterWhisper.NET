// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Tests for ModelDownloader model name mapping and path resolution.
    /// </summary>
    public class ModelDownloaderTests
    {
        [Theory]
        [InlineData("tiny", "Systran/faster-whisper-tiny")]
        [InlineData("base", "Systran/faster-whisper-base")]
        [InlineData("small", "Systran/faster-whisper-small")]
        [InlineData("medium", "Systran/faster-whisper-medium")]
        [InlineData("large-v1", "Systran/faster-whisper-large-v1")]
        [InlineData("large-v2", "Systran/faster-whisper-large-v2")]
        [InlineData("large-v3", "Systran/faster-whisper-large-v3")]
        [InlineData("large-v3-turbo", "Systran/faster-whisper-large-v3-turbo")]
        public void ResolveModelNameOrPath_ShorthandNames_ResolvesCorrectRepoId(string shorthand, string expectedRepo)
        {
            // Use reflection to access the internal mapping since ResolveModelNameOrPath
            // may trigger downloads — we test the mapping logic only
            string result = ModelDownloader.ResolveRepoId(shorthand);
            Assert.Equal(expectedRepo, result);
        }

        [Fact]
        public void ResolveModelNameOrPath_CustomPath_ReturnsPathUnchanged()
        {
            string customPath = @"C:\models\my-custom-whisper";
            string result = ModelDownloader.ResolveRepoId(customPath);
            // Custom paths should be returned as-is (treated as repo ID)
            Assert.Equal(customPath, result);
        }

        [Fact]
        public void GetModelDirectory_ReturnsConsistentPath()
        {
            string dir1 = ModelDownloader.GetModelDirectory("tiny");
            string dir2 = ModelDownloader.GetModelDirectory("tiny");
            Assert.Equal(dir1, dir2);
            Assert.Contains("tiny", dir1);
        }

        [Fact]
        public async Task GetModelPathAsync_HashVerificationFailure_ThrowsAndDeletes()
        {
            string tempCacheDir = Path.Combine(Path.GetTempPath(), "qourex-test-cache-" + Guid.NewGuid());
            string targetDir = Path.Combine(tempCacheDir, "tiny");
            Directory.CreateDirectory(targetDir);

            string modelBinPath = Path.Combine(targetDir, "model.bin");
            File.WriteAllText(modelBinPath, "corrupted or dummy binary content");
            File.WriteAllText(Path.Combine(targetDir, "config.json"), "{}");
            File.WriteAllText(Path.Combine(targetDir, "vocabulary.txt"), "hello");

            var downloader = new ModelDownloader(tempCacheDir);

            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(async () =>
            {
                await downloader.GetModelPathAsync("tiny");
            });

            Assert.False(File.Exists(modelBinPath), "Corrupted model.bin was not deleted!");

            try
            {
                Directory.Delete(tempCacheDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
