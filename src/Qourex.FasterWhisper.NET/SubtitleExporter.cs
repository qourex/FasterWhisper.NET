// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Exports transcription segments to various subtitle and transcript formats:
    /// SRT, WebVTT, TSV, JSON, Markdown, HTML, and word-level karaoke SRT.
    /// </summary>
    public static class SubtitleExporter
    {
        // ─── SRT ────────────────────────────────────────────────────────────
        /// <summary>Converts segments to SRT subtitle format.</summary>
        public static string ToSrt(IEnumerable<WhisperSegment> segments)
        {
            var sb = new StringBuilder();
            int index = 1;
            foreach (var seg in segments)
            {
                sb.AppendLine(index.ToString());
                sb.AppendLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
                sb.AppendLine(seg.Text.Trim());
                sb.AppendLine();
                index++;
            }
            return sb.ToString();
        }

        /// <summary>Writes segments to an SRT file.</summary>
        public static void WriteSrt(IEnumerable<WhisperSegment> segments, string path)
            => File.WriteAllText(path, ToSrt(segments), Encoding.UTF8);

        // ─── WebVTT ─────────────────────────────────────────────────────────
        /// <summary>Converts segments to WebVTT subtitle format.</summary>
        public static string ToVtt(IEnumerable<WhisperSegment> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine();
            foreach (var seg in segments)
            {
                sb.AppendLine($"{FormatVttTime(seg.Start)} --> {FormatVttTime(seg.End)}");
                sb.AppendLine(seg.Text.Trim());
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Writes segments to a WebVTT file.</summary>
        public static void WriteVtt(IEnumerable<WhisperSegment> segments, string path)
            => File.WriteAllText(path, ToVtt(segments), Encoding.UTF8);

        // ─── TSV ────────────────────────────────────────────────────────────
        /// <summary>Converts segments to tab-separated values (start\tend\ttext).</summary>
        public static string ToTsv(IEnumerable<WhisperSegment> segments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("start\tend\ttext");
            foreach (var seg in segments)
            {
                long startMs = (long)Math.Round(seg.Start * 1000.0);
                long endMs = (long)Math.Round(seg.End * 1000.0);
                sb.AppendLine($"{startMs}\t{endMs}\t{seg.Text.Trim()}");
            }
            return sb.ToString();
        }

        // ─── JSON ───────────────────────────────────────────────────────────
        /// <summary>Converts segments to JSON format.</summary>
        public static string ToJson(IEnumerable<WhisperSegment> segments, bool indented = true)
        {
            var data = segments.Select(s => new
            {
                id = s.Id,
                start = Math.Round(s.Start, 3),
                end = Math.Round(s.End, 3),
                text = s.Text.Trim(),
                confidence = Math.Round(s.Confidence, 3),
                temperature = Math.Round(s.Temperature, 2),
                avg_logprob = Math.Round(s.AvgLogProb, 4),
                compression_ratio = Math.Round(s.CompressionRatio, 3),
                no_speech_prob = Math.Round(s.NoSpeechProb, 4),
                language = s.Language,
                words = s.Words?.Select(w => new
                {
                    word = w.Word,
                    start = Math.Round(w.Start, 3),
                    end = Math.Round(w.End, 3),
                    probability = Math.Round(w.Probability, 4)
                })
            });

            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(data, options);
        }

        /// <summary>Writes segments to a JSON file.</summary>
        public static void WriteJson(IEnumerable<WhisperSegment> segments, string path, bool indented = true)
            => File.WriteAllText(path, ToJson(segments, indented), Encoding.UTF8);

        // ─── Word-Level Karaoke SRT (E-15) ──────────────────────────────────
        /// <summary>
        /// Converts segments to word-level SRT with one word per subtitle entry.
        /// Useful for karaoke-style highlighting.
        /// </summary>
        public static string ToWordLevelSrt(IEnumerable<WhisperSegment> segments)
        {
            var sb = new StringBuilder();
            int index = 1;
            foreach (var seg in segments)
            {
                if (seg.Words == null || seg.Words.Count == 0)
                {
                    // Fallback to segment-level
                    sb.AppendLine(index.ToString());
                    sb.AppendLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
                    sb.AppendLine(seg.Text.Trim());
                    sb.AppendLine();
                    index++;
                    continue;
                }

                foreach (var word in seg.Words)
                {
                    if (string.IsNullOrWhiteSpace(word.Word)) continue;
                    sb.AppendLine(index.ToString());
                    sb.AppendLine($"{FormatSrtTime(word.Start)} --> {FormatSrtTime(word.End)}");
                    sb.AppendLine(word.Word.Trim());
                    sb.AppendLine();
                    index++;
                }
            }
            return sb.ToString();
        }

        /// <summary>Writes word-level SRT to a file.</summary>
        public static void WriteWordLevelSrt(IEnumerable<WhisperSegment> segments, string path)
            => File.WriteAllText(path, ToWordLevelSrt(segments), Encoding.UTF8);

        // ─── Markdown (E-14) ────────────────────────────────────────────────
        /// <summary>
        /// Converts segments to Markdown transcript format.
        /// </summary>
        /// <param name="segments">Segments to export.</param>
        /// <param name="includeTimestamps">Include timestamps before each line. Default true.</param>
        /// <returns>Markdown-formatted transcript.</returns>
        public static string ToMarkdown(IEnumerable<WhisperSegment> segments, bool includeTimestamps = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Transcript");
            sb.AppendLine();

            foreach (var seg in segments)
            {
                if (includeTimestamps)
                {
                    sb.AppendLine($"**[{FormatReadableTime(seg.Start)} → {FormatReadableTime(seg.End)}]** {seg.Text.Trim()}");
                }
                else
                {
                    sb.AppendLine(seg.Text.Trim());
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Writes Markdown transcript to a file.</summary>
        public static void WriteMarkdown(IEnumerable<WhisperSegment> segments, string path, bool includeTimestamps = true)
            => File.WriteAllText(path, ToMarkdown(segments, includeTimestamps), Encoding.UTF8);

        // ─── HTML (E-14) ────────────────────────────────────────────────────
        /// <summary>
        /// Converts segments to an HTML transcript with optional low-confidence highlighting.
        /// </summary>
        /// <param name="segments">Segments to export.</param>
        /// <param name="highlightLowConfidence">Highlight segments with confidence &lt; 0.5 in yellow.</param>
        /// <returns>HTML transcript.</returns>
        public static string ToHtml(IEnumerable<WhisperSegment> segments, bool highlightLowConfidence = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset=\"UTF-8\">");
            sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine("  <title>Transcript</title>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body { font-family: 'Segoe UI', system-ui, sans-serif; max-width: 800px; margin: 2em auto; padding: 0 1em; line-height: 1.6; color: #333; }");
            sb.AppendLine("    .segment { margin-bottom: 1em; padding: 0.5em; border-radius: 4px; }");
            sb.AppendLine("    .timestamp { color: #666; font-size: 0.85em; font-family: monospace; }");
            sb.AppendLine("    .low-confidence { background-color: #fff3cd; border-left: 3px solid #ffc107; }");
            sb.AppendLine("    .text { margin-top: 0.2em; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<h1>Transcript</h1>");

            foreach (var seg in segments)
            {
                string cssClass = highlightLowConfidence && seg.Confidence < 0.5f ? "segment low-confidence" : "segment";
                sb.AppendLine($"<div class=\"{cssClass}\">");
                sb.AppendLine($"  <span class=\"timestamp\">[{FormatReadableTime(seg.Start)} → {FormatReadableTime(seg.End)}]</span>");
                sb.AppendLine($"  <div class=\"text\">{System.Net.WebUtility.HtmlEncode(seg.Text.Trim())}</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        /// <summary>Writes HTML transcript to a file.</summary>
        public static void WriteHtml(IEnumerable<WhisperSegment> segments, string path, bool highlightLowConfidence = true)
            => File.WriteAllText(path, ToHtml(segments, highlightLowConfidence), Encoding.UTF8);

        // ─── Helpers ────────────────────────────────────────────────────────
        private static string FormatSrtTime(float seconds)
        {
            double roundedMs = Math.Round(seconds * 1000.0);
            TimeSpan ts = TimeSpan.FromMilliseconds(Math.Max(0, roundedMs));
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        private static string FormatVttTime(float seconds)
        {
            double roundedMs = Math.Round(seconds * 1000.0);
            TimeSpan ts = TimeSpan.FromMilliseconds(Math.Max(0, roundedMs));
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private static string FormatReadableTime(float seconds)
        {
            double roundedMs = Math.Round(seconds * 1000.0);
            TimeSpan ts = TimeSpan.FromMilliseconds(Math.Max(0, roundedMs));
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
