using System;
using System.Linq;
using ActualChat.Mathematics;

namespace ActualChat.Transcription
{
    public sealed class StablePrefixCutter
    {
        private string _previous = string.Empty;
        private double _previousEnd = 0;
        private bool _finalResults;

        public (string Text, int TextIndex, double StartOffset, double Duration) CutMemoized((string Text, double EndOffset) candidate)
        {
            var (text, endOffset) = candidate;
            // special case for final transcription results (words)
            if (_previousEnd - endOffset > 0.3d) {
                _previous = text;
                _previousEnd = endOffset;
                _finalResults = true;
                return (text, 0, 0, endOffset);
            }

            if (_finalResults) {
                var duration = Math.Round(endOffset - _previousEnd, 3, MidpointRounding.AwayFromZero);
                var startOffset = _previousEnd;
                var textIndex = _previous.Length;
                _previous = $"{_previous} {text}";
                _previousEnd = endOffset;
                return ($" {text}", textIndex, startOffset, duration);
            }

            // special case for duplicates
            if (text.Length < _previous.Length) {
                var textIndex = _previous.LastIndexOf(text, StringComparison.InvariantCultureIgnoreCase);
                if (textIndex >= 0) {
                    _previous = $"{_previous[..textIndex]}{text}";
                    _previousEnd = endOffset;
                    var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                    var mappedOffset = map.Map(textIndex);
                    var startOffset = mappedOffset.HasValue 
                        ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                        : 0;
                    var duration = Math.Round(endOffset - startOffset, 3, MidpointRounding.AwayFromZero);
                    return (text, textIndex, startOffset, duration);
                }
            }
            
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