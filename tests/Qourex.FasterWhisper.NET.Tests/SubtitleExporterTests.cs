// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class SubtitleExporterTests
    {
        private static WhisperSegment Seg(float start, float end, string text) =>
            new(text, Array.Empty<int>(), 0f, 0f, start, end);

        private static List<WhisperSegment> CreateTestSegments() => new()
        {
            Seg(0.0f, 5.2f, "Hello, welcome to this tutorial."),
            Seg(5.5f, 10.1f, "Today we'll learn about speech recognition."),
            Seg(11.0f, 15.3f, "Let's get started.")
        };

        [Fact]
        public void ToSrt_ReturnsValidSrtFormat()
        {
            var segments = CreateTestSegments();
            string srt = SubtitleExporter.ToSrt(segments).Replace("\r\n", "\n");

            Assert.Contains("1\n00:00:00,000 --> 00:00:05,200", srt);
            Assert.Contains("Hello, welcome to this tutorial.", srt);
            Assert.Contains("2\n00:00:05,500 --> 00:00:10,100", srt);
            Assert.Contains("3\n", srt);
        }

        [Fact]
        public void ToVtt_ContainsWebVttHeader()
        {
            var segments = CreateTestSegments();
            string vtt = SubtitleExporter.ToVtt(segments);

            Assert.StartsWith("WEBVTT", vtt);
            Assert.Contains("00:00:00.000 --> 00:00:05.200", vtt);
        }

        [Fact]
        public void ToTsv_ReturnsTabSeparatedValues()
        {
            var segments = CreateTestSegments();
            string tsv = SubtitleExporter.ToTsv(segments);

            Assert.Contains("start\tend\ttext", tsv);
            Assert.Contains("0\t5200\tHello, welcome to this tutorial.", tsv);
        }

        [Fact]
        public void ToJson_ReturnsValidJson()
        {
            var segments = CreateTestSegments();
            string json = SubtitleExporter.ToJson(segments);

            Assert.Contains("\"text\":", json);
            Assert.Contains("\"start\":", json);
            Assert.Contains("\"end\":", json);
        }

        [Fact]
        public void ToMarkdown_IncludesTimestamps()
        {
            var segments = CreateTestSegments();
            string md = SubtitleExporter.ToMarkdown(segments);

            Assert.Contains("Hello, welcome to this tutorial.", md);
        }

        [Fact]
        public void ToHtml_ContainsHtmlStructure()
        {
            var segments = CreateTestSegments();
            string html = SubtitleExporter.ToHtml(segments);

            Assert.Contains("<div", html);
            Assert.Contains("Hello, welcome to this tutorial.", html);
        }

        [Fact]
        public void ToWordLevelSrt_GeneratesPerWordEntries()
        {
            var seg = new WhisperSegment("Hello world", Array.Empty<int>(), 0f, 0f, 0f, 2f);
            seg.Words = new List<WhisperWord>
            {
                new() { Start = 0.0f, End = 0.5f, Word = "Hello", Probability = 0.9f },
                new() { Start = 0.5f, End = 1.0f, Word = "world", Probability = 0.8f }
            };

            string srt = SubtitleExporter.ToWordLevelSrt(new List<WhisperSegment> { seg });
            Assert.Contains("Hello", srt);
            Assert.Contains("world", srt);
        }

        [Fact]
        public void ToSrt_EmptySegments_ReturnsEmptyString()
        {
            string srt = SubtitleExporter.ToSrt(new List<WhisperSegment>());
            Assert.Equal(string.Empty, srt);
        }

        [Fact]
        public void WriteSrt_CreatesFile()
        {
            var segments = CreateTestSegments();
            string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.srt");
            try
            {
                SubtitleExporter.WriteSrt(segments, path);
                Assert.True(File.Exists(path));
                string content = File.ReadAllText(path);
                Assert.Contains("Hello, welcome to this tutorial.", content);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
