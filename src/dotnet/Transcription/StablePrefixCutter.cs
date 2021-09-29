using System;
using System.Linq;
using ActualChat.Mathematics;

namespace ActualChat.Transcription
{
    public sealed class StablePrefixCutter
    {
        private string _previous = string.Empty;
        private double _previousEnd;
        private double _previousFinalEnd;
        private int _previousFinalLength;

        public TranscriptSpeechFragment CutMemoized((string Text, double EndOffset) candidate)
            => CutMemoized(
                new TranscriptSpeechFragment {
                    Index = 0,
                    Text = candidate.Text,
                    Duration = candidate.EndOffset
                });
                
        public TranscriptSpeechFragment CutMemoized(TranscriptSpeechFragment speechFragment)
        {
            var (index, _, duration, text, _, isFinal) = speechFragment;

            if (isFinal) {
                var textIndex = _previousFinalLength;
                var startOffset = _previousFinalEnd;
                _previous = string.Empty;
                _previousEnd = 0;
                _previousFinalLength += text.Length;
                _previousFinalEnd += duration;
                return new TranscriptSpeechFragment {
                    Index = index,
                    StartOffset = startOffset,
                    Duration = duration,
                    Text = text,
                    TextIndex = textIndex,
                    SpeakerId = speechFragment.SpeakerId,
                    Confidence = speechFragment.Confidence,
                    IsFinal = true
                };
            }

            // special case for duplicates
            if (text.Length < _previous.Length) {
                var textIndex = _previous.LastIndexOf(text, StringComparison.InvariantCultureIgnoreCase);
                if (textIndex >= 0) {
                    _previous = $"{_previous[..textIndex]}{text}";
                    _previousEnd = duration;
                    var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                    var mappedOffset = map.Map(textIndex);
                    var diffStartOffset = mappedOffset.HasValue 
                        ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                        : 0;
                    var diffDuration = Math.Round(duration - diffStartOffset, 3, MidpointRounding.AwayFromZero);
                    return new TranscriptSpeechFragment {
                        Index = index,
                        StartOffset = _previousFinalEnd + diffStartOffset,
                        Duration = diffDuration,
                        Text = text,
                        TextIndex = _previousFinalLength + textIndex,
                        SpeakerId = speechFragment.SpeakerId,
                        Confidence = speechFragment.Confidence,
                        IsFinal = false
                    };
                }
            }
            
            var (_, diffIndex) = _previous
                .Zip(text)
                .Select((x, i) => (Match:x.First == x.Second, Index:i))
                .Append((Match:false, Index:_previous.Length))
                .SkipWhile(x => x.Match)
                .FirstOrDefault();
            var result = new TranscriptSpeechFragment {
                Index = index,
                StartOffset = _previousFinalEnd,
                Duration = duration,
                Text = text,
                TextIndex = _previousFinalLength,
                SpeakerId = speechFragment.SpeakerId,
                Confidence = speechFragment.Confidence,
                IsFinal = false
            };
            
            if (diffIndex > 0 && diffIndex < text.Length) {
                var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                var mappedOffset = map.Map(diffIndex);
                var diffStartOffset = mappedOffset.HasValue 
                    ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                    : 0;
                var diffDuration = Math.Round(duration - diffStartOffset, 3, MidpointRounding.AwayFromZero);
                var newText = text[diffIndex..];
                result = new TranscriptSpeechFragment {
                    Index = index,
                    StartOffset = _previousFinalEnd + diffStartOffset,
                    Duration = diffDuration,
                    Text = newText,
                    TextIndex = _previousFinalLength + diffIndex,
                    SpeakerId = speechFragment.SpeakerId,
                    Confidence = speechFragment.Confidence,
                    IsFinal = false
                };
            }
            
            _previous = text;
            _previousEnd = duration;
            return result;
        }
    }
}