using System;
using System.Linq;
using ActualChat.Mathematics;

namespace ActualChat.Transcription
{
    public sealed class StablePrefixCutter
    {
        private string _previous = string.Empty;
        private double _previousEnd = 0;

        public (string Text, int TextIndex, double StartOffset, double Duration) CutMemoized((string Text, double EndOffset) candidate)
        {
            var (text, endOffset) = candidate;
            var (_, index) = _previous
                .Zip(text)
                .Select((x, i) => (Match:x.First == x.Second, Index:i))
                .Append((Match:false, Index:_previous.Length))
                .SkipWhile(x => x.Match)
                .FirstOrDefault();
            var result = (text, 0, 0.0, endOffset);
            if (index > 0 && index < text.Length) {
                var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                var mappedOffset = map.Map(index);
                var startOffset = mappedOffset.HasValue 
                    ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                    : 0;
                var duration = Math.Round(endOffset - startOffset, 3, MidpointRounding.AwayFromZero);
                var newText = text[index..];
                result = (newText, index, startOffset, duration);
            }
            
            _previous = text;
            _previousEnd = endOffset;
            return result;
        }
    }
}