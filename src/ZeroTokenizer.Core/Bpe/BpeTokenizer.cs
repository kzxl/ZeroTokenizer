using System;
using System.Collections.Generic;
using System.Text;
using ZeroTokenizer.Core.Abstractions;

namespace ZeroTokenizer.Core.Bpe
{
    /// <summary>
    /// Pure C# Byte-Pair Encoding (BPE) tokenizer with byte-level fallback.
    /// Guarantees that any UTF-8 string can be tokenized and restored losslessly without out-of-vocabulary (OOV) errors.
    /// </summary>
    public class BpeTokenizer : ITokenizer
    {
        private readonly Dictionary<string, int> _tokenToId;
        private readonly Dictionary<int, string> _idToToken;
        private readonly Dictionary<(int, int), int> _bpeRanks;
        private readonly int _maxTokenId;

        public int VocabularySize => _tokenToId.Count;

        public BpeTokenizer(
            Dictionary<string, int> vocabulary,
            Dictionary<(int, int), int> bpeRanks)
        {
            _tokenToId = new Dictionary<string, int>(vocabulary);
            _idToToken = new Dictionary<int, string>(vocabulary.Count);
            _bpeRanks = new Dictionary<(int, int), int>(bpeRanks);

            int maxId = 0;
            foreach (var kvp in vocabulary)
            {
                _idToToken[kvp.Value] = kvp.Key;
                if (kvp.Value > maxId) maxId = kvp.Value;
            }
            _maxTokenId = maxId;

            // Ensure all 256 byte tokens exist
            EnsureByteTokens();
        }

        private void EnsureByteTokens()
        {
            for (int b = 0; b < 256; b++)
            {
                string byteStr = $"<0x{b:X2}>";
                if (!_tokenToId.ContainsKey(byteStr))
                {
                    int id = _tokenToId.Count;
                    _tokenToId[byteStr] = id;
                    _idToToken[id] = byteStr;
                }
            }
        }

        public int CountTokens(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return 0;

            // Fast path approximation for count or full tokenization into a pooled buffer
            var tokens = EncodeInternal(text);
            return tokens.Count;
        }

        public int Encode(ReadOnlySpan<char> text, Span<int> destinationTokenIds)
        {
            if (text.IsEmpty) return 0;

            var tokens = EncodeInternal(text);
            if (destinationTokenIds.Length < tokens.Count)
                throw new ArgumentException($"Destination span too small. Required: {tokens.Count}, Available: {destinationTokenIds.Length}");

            for (int i = 0; i < tokens.Count; i++)
            {
                destinationTokenIds[i] = tokens[i];
            }
            return tokens.Count;
        }

        public int[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();

            var tokens = EncodeInternal(text.AsSpan());
            return tokens.ToArray();
        }

        public string Decode(ReadOnlySpan<int> tokenIds)
        {
            if (tokenIds.IsEmpty) return string.Empty;

            var sb = new StringBuilder();
            var byteBuffer = new List<byte>();

            for (int i = 0; i < tokenIds.Length; i++)
            {
                int id = tokenIds[i];
                if (_idToToken.TryGetValue(id, out string? token))
                {
                    if (token.StartsWith("<0x") && token.EndsWith(">") && token.Length == 6)
                    {
                        // Byte fallback
                        if (byte.TryParse(token.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                        {
                            byteBuffer.Add(b);
                            continue;
                        }
                    }

                    // Flush any pending byte buffer
                    FlushBytes(sb, byteBuffer);
                    sb.Append(token);
                }
            }

            FlushBytes(sb, byteBuffer);
            return sb.ToString();
        }

        public int Decode(ReadOnlySpan<int> tokenIds, Span<char> destinationText)
        {
            string decoded = Decode(tokenIds);
            if (destinationText.Length < decoded.Length)
                throw new ArgumentException("Destination span too small.");

            decoded.AsSpan().CopyTo(destinationText);
            return decoded.Length;
        }

        private static void FlushBytes(StringBuilder sb, List<byte> byteBuffer)
        {
            if (byteBuffer.Count > 0)
            {
                string text = Encoding.UTF8.GetString(byteBuffer.ToArray());
                sb.Append(text);
                byteBuffer.Clear();
            }
        }

        private List<int> EncodeInternal(ReadOnlySpan<char> text)
        {
            var result = new List<int>();
            if (text.IsEmpty) return result;

            // 1. Initial word splitting by whitespace and punctuation
            int start = 0;
            while (start < text.Length)
            {
                // Find next token segment
                int end = start;
                bool isSpace = char.IsWhiteSpace(text[start]);

                while (end < text.Length && char.IsWhiteSpace(text[end]) == isSpace)
                {
                    end++;
                }

                string segment = text.Slice(start, end - start).ToString();
                start = end;

                // 2. Check if segment itself is a known token
                if (_tokenToId.TryGetValue(segment, out int directId))
                {
                    result.Add(directId);
                    continue;
                }

                // 3. Convert segment to UTF-8 bytes and BPE merge
                byte[] bytes = Encoding.UTF8.GetBytes(segment);
                var wordTokens = new List<int>(bytes.Length);
                for (int b = 0; b < bytes.Length; b++)
                {
                    string byteStr = $"<0x{bytes[b]:X2}>";
                    if (_tokenToId.TryGetValue(byteStr, out int bId))
                    {
                        wordTokens.Add(bId);
                    }
                }

                // 4. Repeatedly merge lowest-rank pairs
                BpeMerge(wordTokens);

                result.AddRange(wordTokens);
            }

            return result;
        }

        private void BpeMerge(List<int> tokens)
        {
            if (tokens.Count <= 1) return;

            while (tokens.Count > 1)
            {
                int bestPairIndex = -1;
                int minRank = int.MaxValue;

                for (int i = 0; i < tokens.Count - 1; i++)
                {
                    var pair = (tokens[i], tokens[i + 1]);
                    if (_bpeRanks.TryGetValue(pair, out int rank))
                    {
                        if (rank < minRank)
                        {
                            minRank = rank;
                            bestPairIndex = i;
                        }
                    }
                }

                if (bestPairIndex == -1)
                {
                    // No more mergeable pairs
                    break;
                }

                // Merge tokens[bestPairIndex] and tokens[bestPairIndex + 1]
                int tokenA = tokens[bestPairIndex];
                int tokenB = tokens[bestPairIndex + 1];

                string strA = _idToToken.TryGetValue(tokenA, out var sa) ? sa : "";
                string strB = _idToToken.TryGetValue(tokenB, out var sb) ? sb : "";
                string merged = strA + strB;

                if (_tokenToId.TryGetValue(merged, out int mergedId))
                {
                    tokens[bestPairIndex] = mergedId;
                    tokens.RemoveAt(bestPairIndex + 1);
                }
                else
                {
                    break;
                }
            }
        }
    }
}
