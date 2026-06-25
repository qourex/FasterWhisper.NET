// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using Xunit;

namespace Qourex.FasterWhisper.NET.Tests
{
    public class TextRestorerTests
    {
        [Fact]
        public void RestoreCapitalization_CapitalizesAfterPeriod()
        {
            string result = TextRestorer.RestoreCapitalization("hello world. this is a test.");
            Assert.StartsWith("Hello", result);
            Assert.Contains("This is a test.", result);
        }

        [Fact]
        public void RestoreCapitalization_FixesStandaloneI()
        {
            string result = TextRestorer.RestoreCapitalization("i think i should go.");
            Assert.Contains("I think", result);
        }

        [Fact]
        public void RestorePunctuation_AddsMissingPeriod()
        {
            string result = TextRestorer.RestorePunctuation("Hello world");
            Assert.EndsWith(".", result);
        }

        [Fact]
        public void RestorePunctuation_DoesNotDoublePunctuate()
        {
            string result = TextRestorer.RestorePunctuation("Hello world.");
            Assert.False(result.EndsWith(".."));
        }

        [Fact]
        public void NormalizeFormatting_FixesCurlyQuotes()
        {
            string result = TextRestorer.NormalizeFormatting("\u201CHello\u201D");
            Assert.Contains("\"Hello\"", result);
        }

        [Fact]
        public void NormalizeFormatting_FixesMultipleSpaces()
        {
            string result = TextRestorer.NormalizeFormatting("Hello    world");
            Assert.DoesNotContain("  ", result);
        }

        [Fact]
        public void Restore_FullPipeline()
        {
            string input = "hello world. i think this is great";
            string result = TextRestorer.Restore(input);

            Assert.StartsWith("Hello", result);
            Assert.Contains("I think", result);
            Assert.EndsWith(".", result);
        }

        [Fact]
        public void Restore_EmptyString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, TextRestorer.Restore(string.Empty));
        }

        [Fact]
        public void RestoreCapitalization_AfterQuestionMark()
        {
            string result = TextRestorer.RestoreCapitalization("what is this? it is a test.");
            Assert.Contains("It is a test.", result);
        }

        [Fact]
        public void RestoreCapitalization_AfterExclamation()
        {
            string result = TextRestorer.RestoreCapitalization("wow! that is amazing.");
            Assert.Contains("That is amazing.", result);
        }
    }
}
