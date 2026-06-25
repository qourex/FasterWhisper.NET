// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class TranscriptionInfoTests
    {
        [Fact]
        public void TranscriptionInfo_CanBeCreated()
        {
            var info = new TranscriptionInfo
            {
                Language = "en",
                LanguageProbability = 0.98f,
                Duration = 30.0f,
                DurationAfterVad = 25.0f,
                TranscriptionOptions = new WhisperOptions(),
                VadOptions = new VadOptions()
            };

            Assert.Equal("en", info.Language);
            Assert.Equal(0.98f, info.LanguageProbability);
            Assert.Equal(30.0f, info.Duration);
        }

        [Fact]
        public void TranscriptionInfo_AllLanguageProbs_NullByDefault()
        {
            var info = new TranscriptionInfo
            {
                Language = "en",
                LanguageProbability = 1.0f,
                Duration = 5.0f,
                DurationAfterVad = 5.0f,
                TranscriptionOptions = new WhisperOptions(),
                VadOptions = new VadOptions()
            };

            Assert.Null(info.AllLanguageProbs);
        }

        [Fact]
        public void TranscriptionInfo_AllLanguageProbs_CanBeSet()
        {
            var info = new TranscriptionInfo
            {
                Language = "en",
                LanguageProbability = 0.9f,
                Duration = 5.0f,
                DurationAfterVad = 5.0f,
                AllLanguageProbs = new List<(string Language, float Probability)>
                {
                    ("en", 0.9f),
                    ("fr", 0.05f),
                    ("de", 0.03f)
                },
                TranscriptionOptions = new WhisperOptions(),
                VadOptions = new VadOptions()
            };

            Assert.Equal(3, info.AllLanguageProbs.Count);
            Assert.Equal("en", info.AllLanguageProbs[0].Language);
        }

        [Fact]
        public void TranscriptionResult_ContainsInfoAndSegments()
        {
            var seg = new WhisperSegment("Hello", Array.Empty<int>(), 0f, 0f, 0f, 5f);
            var result = new TranscriptionResult
            {
                Segments = new List<WhisperSegment> { seg },
                Info = new TranscriptionInfo
                {
                    Language = "en",
                    LanguageProbability = 1.0f,
                    Duration = 5.0f,
                    DurationAfterVad = 5.0f,
                    TranscriptionOptions = new WhisperOptions(),
                    VadOptions = new VadOptions()
                }
            };

            Assert.NotNull(result.Info);
            Assert.NotNull(result.Segments);
        }
    }
}
