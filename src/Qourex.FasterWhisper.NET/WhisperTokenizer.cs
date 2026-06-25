// Copyright (c) 2026 Qourex. Licensed under the MIT License.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Qourex.FasterWhisper.NET
{
    /// <summary>
    /// Implements Whisper's Byte-Pair Encoding (BPE) tokenizer and decoder, supporting dynamic vocabulary loading.
    /// </summary>
    public class WhisperTokenizer
    {
        private readonly List<string> _vocabulary;
        private readonly Dictionary<string, int> _tokenToId;
        private readonly Dictionary<char, byte> _unicodeToBytesMap;
        private static readonly Dictionary<byte, char> s_bytesToUnicode = BuildBytesToUnicodeMap();

        // Special token cache
        /// <summary>Gets the token ID for the end-of-text token.</summary>
        public int EndOfTextId { get; }
        /// <summary>Gets the token ID for the start-of-transcript token.</summary>
        public int StartOfTranscriptId { get; }
        /// <summary>Gets the token ID for the transcribe task token.</summary>
        public int TranscribeId { get; }
        /// <summary>Gets the token ID for the translate task token.</summary>
        public int TranslateId { get; }
        /// <summary>Gets the token ID for the no-timestamps token.</summary>
        public int NoTimestampsId { get; }
        /// <summary>Gets the token ID for the no-speech token.</summary>
        public int NoSpeechId { get; }
        /// <summary>Gets the token ID for the start-of-prev token.</summary>
        public int StartOfPrevId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WhisperTokenizer"/> class from in-memory vocabulary content.
        /// </summary>
        /// <param name="vocabContent">The string content of the vocabulary file.</param>
        /// <param name="isJson">True if the content is in JSON format; false if it is standard line-by-line text format.</param>
        public WhisperTokenizer(string vocabContent, bool isJson = false)
        {
            if (isJson)
            {
                _vocabulary = JsonSerializer.Deserialize<List<string>>(vocabContent) ?? throw new InvalidDataException("Invalid vocabulary.json");
            }
            else
            {
                // split by newline
                string[] lines = vocabContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                _vocabulary = new List<string>(lines);
            }

            _tokenToId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _vocabulary.Count; i++)
            {
                _tokenToId[_vocabulary[i]] = i;
            }

            _unicodeToBytesMap = BuildUnicodeToBytesMap();

            EndOfTextId = GetTokenId("<|endoftext|>");
            StartOfTranscriptId = GetTokenId("<|startoftranscript|>");
            TranscribeId = GetTokenId("<|transcribe|>");
            TranslateId = GetTokenId("<|translate|>");
            NoTimestampsId = GetTokenId("<|notimestamps|>");
            NoSpeechId = GetTokenId("<|nospeech|>");
            StartOfPrevId = GetTokenId("<|startofprev|>");
        }

        /// <summary>
        /// Loads the Whisper vocabulary from the model directory.
        /// </summary>
        /// <param name="modelPath">Path to the model directory containing vocabulary.txt or vocabulary.json.</param>
        public WhisperTokenizer(string modelPath)
        {
            // Try vocabulary.txt first (CTranslate2 format: one token per line)
            string vocabTxtPath = Path.Combine(modelPath, "vocabulary.txt");
            string vocabJsonPath = Path.Combine(modelPath, "vocabulary.json");

            if (File.Exists(vocabTxtPath))
            {
                string[] lines = File.ReadAllLines(vocabTxtPath);
                _vocabulary = new List<string>(lines);
            }
            else if (File.Exists(vocabJsonPath))
            {
                string json = File.ReadAllText(vocabJsonPath);
                _vocabulary = JsonSerializer.Deserialize<List<string>>(json) ?? throw new InvalidDataException("Invalid vocabulary.json");
            }
            else
            {
                throw new FileNotFoundException($"No vocabulary file found in model directory. Expected vocabulary.txt or vocabulary.json at: {modelPath}");
            }

            _tokenToId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _vocabulary.Count; i++)
            {
                _tokenToId[_vocabulary[i]] = i;
            }

            _unicodeToBytesMap = BuildUnicodeToBytesMap();

            // Resolve special tokens dynamically
            EndOfTextId = GetTokenId("<|endoftext|>");
            StartOfTranscriptId = GetTokenId("<|startoftranscript|>");
            TranscribeId = GetTokenId("<|transcribe|>");
            TranslateId = GetTokenId("<|translate|>");
            NoTimestampsId = GetTokenId("<|notimestamps|>");
            NoSpeechId = GetTokenId("<|nospeech|>");
            StartOfPrevId = GetTokenId("<|startofprev|>");
        }

        /// <summary>
        /// Gets the token ID for a specific token string.
        /// </summary>
        /// <param name="token">Token string (e.g., "&lt;|en|&gt;").</param>
        /// <returns>Token ID or -1 if not found.</returns>
        public int GetTokenId(string token)
        {
            return _tokenToId.TryGetValue(token, out int id) ? id : -1;
        }

        /// <summary>
        /// Gets the token string representation for a specific token ID.
        /// </summary>
        public string GetTokenString(int id)
        {
            if (id < 0 || id >= _vocabulary.Count)
                return $"[Token_{id}]";
            return _vocabulary[id];
        }

        /// <summary>
        /// Decodes a sequence of token IDs into a clean text string.
        /// </summary>
        /// <param name="tokenIds">Sequence of token IDs.</param>
        /// <param name="skipSpecialTokens">True to filter out special tokens (like start, end, timestamp tokens).</param>
        /// <returns>Decoded text.</returns>
        public string Decode(IEnumerable<int> tokenIds, bool skipSpecialTokens = true)
        {
            var byteBuffer = new List<byte>();

            foreach (int id in tokenIds)
            {
                string tokenStr = GetTokenString(id);

                // Skip special tokens if requested
                if (skipSpecialTokens && IsSpecialToken(tokenStr))
                {
                    continue;
                }

                // Decode token characters back to bytes
                foreach (char c in tokenStr)
                {
                    if (_unicodeToBytesMap.TryGetValue(c, out byte b))
                    {
                        byteBuffer.Add(b);
                    }
                    else
                    {
                        // Fallback for standard characters if they slip through
                        byteBuffer.Add((byte)c);
                    }
                }
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        /// <summary>
        /// Determines if a token string represents a Whisper special token or timestamp token.
        /// </summary>
        public static bool IsSpecialToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            // Timestamp tokens look like "<|0.00|>", "<|30.00|>", etc.
            if (token.StartsWith("<|") && token.EndsWith("|>"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the token ID where timestamp tokens begin (first timestamp token in vocab).
        /// </summary>
        public int TimestampBeginId
        {
            get
            {
                // Timestamp tokens start at <|0.00|> — find it dynamically
                int id = GetTokenId("<|0.00|>");
                return id >= 0 ? id : -1;
            }
        }

        private static Dictionary<char, byte> BuildUnicodeToBytesMap()
        {
            var map = new Dictionary<char, byte>();
            var allowedBytes = new HashSet<int>();

            // Ranges from GPT-2 standard bytes_to_unicode
            for (int i = 33; i <= 126; i++) allowedBytes.Add(i);
            for (int i = 161; i <= 172; i++) allowedBytes.Add(i);
            for (int i = 174; i <= 255; i++) allowedBytes.Add(i);

            int n = 256;
            for (int i = 0; i < 256; i++)
            {
                if (!allowedBytes.Contains(i))
                {
                    map.Add((char)n, (byte)i);
                    n++;
                }
                else
                {
                    map.Add((char)i, (byte)i);
                }
            }

            return map;
        }

        private static Dictionary<byte, char> BuildBytesToUnicodeMap()
        {
            var forward = new Dictionary<byte, char>();
            var allowedBytes = new HashSet<int>();

            for (int i = 33; i <= 126; i++) allowedBytes.Add(i);
            for (int i = 161; i <= 172; i++) allowedBytes.Add(i);
            for (int i = 174; i <= 255; i++) allowedBytes.Add(i);

            int n = 256;
            for (int i = 0; i < 256; i++)
            {
                if (!allowedBytes.Contains(i))
                {
                    forward.Add((byte)i, (char)n);
                    n++;
                }
                else
                {
                    forward.Add((byte)i, (char)i);
                }
            }

            return forward;
        }

        /// <summary>
        /// Encodes text into a sequence of BPE token IDs using the GPT-2 byte-level BPE algorithm.
        /// This is the inverse of <see cref="Decode"/>.
        /// </summary>
        /// <param name="text">The text to encode.</param>
        /// <returns>A list of token IDs representing the encoded text.</returns>
        public List<int> Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<int>();

            var bytesToUnicode = s_bytesToUnicode;

            // Convert text bytes to GPT-2 unicode representation
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            var unicodeChars = new StringBuilder(textBytes.Length);
            foreach (byte b in textBytes)
            {
                unicodeChars.Append(bytesToUnicode[b]);
            }

            string encoded = unicodeChars.ToString();
            var result = new List<int>();

#if NET9_0_OR_GREATER
            var lookup = _tokenToId.GetAlternateLookup<ReadOnlySpan<char>>();
#endif
            ReadOnlySpan<char> encodedSpan = encoded.AsSpan();

            int pos = 0;
            while (pos < encodedSpan.Length)
            {
                int bestLen = 0;
                int bestId = -1;

                // Try progressively shorter substrings from current position
                int maxLen = Math.Min(encodedSpan.Length - pos, 50); // Max token length heuristic
                for (int len = maxLen; len >= 1; len--)
                {
                    ReadOnlySpan<char> candidate = encodedSpan.Slice(pos, len);
#if NET9_0_OR_GREATER
                    if (lookup.TryGetValue(candidate, out int id))
                    {
                        bestLen = len;
                        bestId = id;
                        break;
                    }
#else
                    if (_tokenToId.TryGetValue(candidate.ToString(), out int id))
                    {
                        bestLen = len;
                        bestId = id;
                        break;
                    }
#endif
                }

                if (bestId >= 0)
                {
                    result.Add(bestId);
                    pos += bestLen;
                }
                else
                {
                    // Single character fallback — skip if not in vocabulary
                    pos++;
                }
            }

            return result;
        }
    }
}
