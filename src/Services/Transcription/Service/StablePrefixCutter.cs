using System;
using System.Linq;
using ActualChat.Mathematics;

namespace ActualChat.Transcription
{
    public sealed class StablePrefixCutter
    {
        private readonly WordPrefixTree<double> _trie = new();

        public (string Text, double StartOffset, double Duration) CutMemoized((string Text, double EndOffset) candidate)
        {
            var (text, endOffset) = candidate;
            var words = text.Split();
            var (prefix, offsets) = _trie.GetCommonPrefix(words);
            var startOffset = Math.Round(offsets.Count > 0 ? offsets.Max() : 0, 3, MidpointRounding.AwayFromZero);
            var duration = Math.Round(endOffset - startOffset, 3, MidpointRounding.AwayFromZero);
            var newText = words.Length == prefix.Count
                ? text
                : string.Join(" ", words.Skip(prefix.Count));
            _trie.Add(words, endOffset);
            return new (newText, startOffset, duration);
        }
    }
}