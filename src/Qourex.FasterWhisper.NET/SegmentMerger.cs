// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Merges small segments into natural paragraphs or splits at sentence boundaries.
    /// </summary>
    public static class SegmentMerger
    {
        /// <summary>
        /// Merges adjacent segments into paragraphs where the pause between them
        /// is less than <paramref name="maxPauseSeconds"/>.
        /// </summary>
        /// <param name="segments">Input segments to merge.</param>
        /// <param name="maxPauseSeconds">Maximum pause between segments to merge them. Default 1.0s.</param>
        /// <param name="maxWordsPerParagraph">Maximum words per merged paragraph. Default 50.</param>
        /// <returns>Merged segments representing paragraphs.</returns>
        public static List<WhisperSegment> MergeIntoParagraphs(
            IEnumerable<WhisperSegment> segments,
            float maxPauseSeconds = 1.0f,
            int maxWordsPerParagraph = 50)
        {
            var result = new List<WhisperSegment>();
            var segList = segments.ToList();
            if (segList.Count == 0) return result;

            WhisperSegment current = CloneSegment(segList[0]);
            int currentWordCount = CountWords(current.Text);

            for (int i = 1; i < segList.Count; i++)
            {
                var next = segList[i];
                float pause = next.Start - current.End;
                int nextWordCount = CountWords(next.Text);

                bool shouldMerge = pause <= maxPauseSeconds
                    && (currentWordCount + nextWordCount) <= maxWordsPerParagraph;

                if (shouldMerge)
                {
                    // Merge: extend end, concatenate text
                    current.End = next.End;
                    current.Text = current.Text.TrimEnd() + " " + next.Text.Trim();
                    currentWordCount += nextWordCount;

                    // Merge words list
                    if (current.Words != null && next.Words != null)
                    {
                        current.Words.AddRange(next.Words);
                    }

                    // Use the worse (lower) confidence
                    current.Confidence = Math.Min(current.Confidence, next.Confidence);
                }
                else
                {
                    result.Add(current);
                    current = CloneSegment(next);
                    currentWordCount = nextWordCount;
                }
            }

            result.Add(current);

            // Re-index
            for (int i = 0; i < result.Count; i++)
            {
                result[i].Id = i;
            }

            return result;
        }

        /// <summary>
        /// Splits segments at sentence boundaries (periods, question marks, exclamation marks).
        /// Each output segment contains exactly one sentence.
        /// </summary>
        /// <param name="segments">Input segments to split.</param>
        /// <returns>Segments split at sentence boundaries.</returns>
        public static List<WhisperSegment> SplitIntoSentences(IEnumerable<WhisperSegment> segments)
        {
            var result = new List<WhisperSegment>();
            int index = 0;

            foreach (var seg in segments)
            {
                string text = seg.Text.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                // Split at sentence-ending punctuation followed by a space or end of string
                var sentences = SplitAtSentenceBoundaries(text);

                if (sentences.Count <= 1)
                {
                    var clone = CloneSegment(seg);
                    clone.Id = index++;
                    result.Add(clone);
                    continue;
                }

                // Distribute timing proportionally by character count
                float totalChars = text.Length;
                float totalDuration = seg.End - seg.Start;
                float currentStart = seg.Start;

                int wordIndex = 0;
                foreach (string sentence in sentences)
                {
                    if (string.IsNullOrWhiteSpace(sentence)) continue;

                    float proportion = sentence.Length / totalChars;
                    float duration = totalDuration * proportion;

                    var sentenceSeg = new WhisperSegment(
                        sentence.Trim(),
                        seg.Tokens,
                        seg.Score,
                        seg.NoSpeechProb,
                        currentStart,
                        currentStart + duration)
                    {
                        Id = index++,
                        Seek = seg.Seek,
                        Temperature = seg.Temperature,
                        Language = seg.Language,
                        AvgLogProb = seg.AvgLogProb,
                        CompressionRatio = seg.CompressionRatio,
                        Confidence = seg.Confidence
                    };

                    if (seg.Words != null && seg.Words.Count > 0)
                    {
                        var sentenceWords = sentence.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        int count = sentenceWords.Length;

                        var wordsList = new List<WhisperWord>();
                        for (int k = 0; k < count && wordIndex < seg.Words.Count; k++)
                        {
                            var w = seg.Words[wordIndex++];
                            wordsList.Add(new WhisperWord
                            {
                                Word = w.Word,
                                Start = w.Start,
                                End = w.End,
                                Probability = w.Probability,
                                Confidence = w.Confidence,
                                IsLowConfidence = w.IsLowConfidence
                            });
                        }
                        sentenceSeg.Words = wordsList;
                    }

                    result.Add(sentenceSeg);
                    currentStart += duration;
                }
            }

            return result;
        }

        private static List<string> SplitAtSentenceBoundaries(string text)
        {
            var sentences = new List<string>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c == '.' || c == '!' || c == '?') && i > 0)
                {
                    // Check it's not an abbreviation (e.g., "Dr.", "Mr.", "U.S.A.")
                    bool isAbbreviation = false;
                    if (c == '.' && i + 1 < text.Length && char.IsLetter(text[i + 1]))
                    {
                        isAbbreviation = true;
                    }
                    if (c == '.' && i >= 2 && text[i - 1] == '.' )
                    {
                        isAbbreviation = true; // Ellipsis
                    }

                    if (!isAbbreviation && (i + 1 >= text.Length || text[i + 1] == ' '))
                    {
                        sentences.Add(text.Substring(start, i - start + 1));
                        start = i + 1;
                        // Skip trailing space
                        if (start < text.Length && text[start] == ' ')
                            start++;
                    }
                }
            }

            // Remaining text
            if (start < text.Length)
            {
                string remaining = text.Substring(start).Trim();
                if (!string.IsNullOrEmpty(remaining))
                    sentences.Add(remaining);
            }

            return sentences;
        }

        private static WhisperSegment CloneSegment(WhisperSegment seg)
        {
            var clone = new WhisperSegment(seg.Text, seg.Tokens, seg.Score, seg.NoSpeechProb, seg.Start, seg.End)
            {
                Id = seg.Id,
                Seek = seg.Seek,
                Temperature = seg.Temperature,
                Language = seg.Language,
                AvgLogProb = seg.AvgLogProb,
                CompressionRatio = seg.CompressionRatio,
                Confidence = seg.Confidence,
                Words = seg.Words != null 
                    ? seg.Words.Select(w => new WhisperWord 
                      { 
                          Word = w.Word, 
                          Start = w.Start, 
                          End = w.End, 
                          Probability = w.Probability,
                          Confidence = w.Confidence,
                          IsLowConfidence = w.IsLowConfidence
                      }).ToList() 
                    : new List<WhisperWord>()
            };
            return clone;
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
