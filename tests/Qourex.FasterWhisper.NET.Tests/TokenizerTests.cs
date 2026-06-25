// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class TokenizerTests : IDisposable
    {
        private readonly string _tempDir;

        public TokenizerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);

            // Write dummy vocabulary.json
            var vocab = new[]
            {
                "<|endoftext|>",
                "<|startoftranscript|>",
                "<|en|>",
                "<|transcribe|>",
                "<|translate|>",
                "<|notimestamps|>",
                "<|nospeech|>",
                "hello",
                "Ġworld"
            };

            string json = JsonSerializer.Serialize(vocab);
            File.WriteAllText(Path.Combine(_tempDir, "vocabulary.json"), json);
        }

        [Fact]
        public void TestTokenResolution()
        {
            var tokenizer = new WhisperTokenizer(_tempDir);

            Assert.Equal(0, tokenizer.EndOfTextId);
            Assert.Equal(1, tokenizer.StartOfTranscriptId);
            Assert.Equal(2, tokenizer.GetTokenId("<|en|>"));
            Assert.Equal(7, tokenizer.GetTokenId("hello"));
        }

        [Fact]
        public void TestDecoderGpt2ByteMapping()
        {
            var tokenizer = new WhisperTokenizer(_tempDir);

            // 7 = "hello", 8 = "Ġworld"
            string decoded = tokenizer.Decode(new[] { 7, 8 });
            
            // "Ġ" should be decoded back to a space character " "
            Assert.Equal("hello world", decoded);
        }

        [Fact]
        public void TestSpecialTokenFiltering()
        {
            var tokenizer = new WhisperTokenizer(_tempDir);

            // Includes <|en|> (id 2) and timestamp-like token (not in vocab, but string form is matched)
            string decoded = tokenizer.Decode(new[] { 2, 7, 8 }, skipSpecialTokens: true);
            Assert.Equal("hello world", decoded);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore
            }
        }
    }
}
