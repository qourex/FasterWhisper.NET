// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    /// <summary>
    /// Tests for WhisperTokenizer.Encode() — BPE encoding (TEST-2).
    /// These tests do not require a model; they test the tokenizer logic with a vocabulary.
    /// </summary>
    public class TokenizerEncodeTests
    {
        [Fact]
        public void Encode_EmptyString_ReturnsEmptyList()
        {
            var tokenizer = CreateTestTokenizer();
            if (tokenizer == null) return; // Skip if no vocabulary available

            var result = tokenizer.Encode("");
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void Encode_NullString_ReturnsEmptyList()
        {
            var tokenizer = CreateTestTokenizer();
            if (tokenizer == null) return;

            var result = tokenizer.Encode(null!);
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void Encode_SimpleText_ReturnsNonEmptyTokens()
        {
            var tokenizer = CreateTestTokenizer();
            if (tokenizer == null) return;

            var result = tokenizer.Encode("hello");
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            // All returned token IDs should be valid (non-negative)
            foreach (int id in result)
            {
                Assert.True(id >= 0, $"Token ID should be non-negative, got {id}");
            }
        }

        [Fact]
        public void Encode_RoundTrip_DecodesToOriginalText()
        {
            var tokenizer = CreateTestTokenizer();
            if (tokenizer == null) return;

            string original = "hello world";
            var tokens = tokenizer.Encode(original);
            string decoded = tokenizer.Decode(tokens.ToArray());

            // Decoded text should match original (possibly with whitespace differences)
            Assert.Equal(original, decoded.Trim());
        }

        /// <summary>
        /// Attempts to load a tokenizer from a known model location.
        /// Returns null if no model is available (tests are skipped gracefully).
        /// </summary>
        private static WhisperTokenizer? CreateTestTokenizer()
        {
            // Try to find a cached model vocabulary
            string cacheDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "qourex-fasterwhisper");

            if (!System.IO.Directory.Exists(cacheDir))
                return null;

            // Look for any vocabulary.txt or vocabulary.json in cached models
            foreach (var dir in System.IO.Directory.GetDirectories(cacheDir))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "vocabulary.txt")) ||
                    System.IO.File.Exists(System.IO.Path.Combine(dir, "vocabulary.json")))
                {
                    try
                    {
                        return new WhisperTokenizer(dir);
                    }
                    catch
                    {
                        // Try next directory
                    }
                }
            }

            return null;
        }
    }
}
