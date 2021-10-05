using ActualChat.Mathematics;

namespace ActualChat.Transcription
{
    public sealed class StablePrefixCutter
    {
        private string _previous = string.Empty;
        private double _previousEnd;
        private double _previousFinalEnd;
        private int _previousFinalLength;

        public TranscriptUpdate CutMemoized((string Text, double EndOffset) candidate)
            => CutMemoized(
                new TranscriptUpdate {
                    Text = candidate.Text,
                    Duration = candidate.EndOffset
                });

        public TranscriptUpdate CutMemoized(TranscriptUpdate update)
        {
            var updateText = update.Text;
            var updateDuration = update.Duration;
            if (update.IsFinal) {
                var textIndex = _previousFinalLength;
                var startOffset = _previousFinalEnd;
                _previous = string.Empty;
                _previousEnd = 0;
                _previousFinalLength += updateText.Length;
                _previousFinalEnd += updateDuration;
                return new TranscriptUpdate {
                    StartOffset = startOffset,
                    Duration = updateDuration,
                    Text = updateText,
                    TextIndex = textIndex,
                    SpeakerId = update.SpeakerId,
                    Confidence = update.Confidence,
                    IsFinal = true
                };
            }

            // special case for duplicates
            if (updateText.Length < _previous.Length) {
                var textIndex = _previous.LastIndexOf(updateText, StringComparison.InvariantCultureIgnoreCase);
                if (textIndex >= 0) {
                    _previous = $"{_previous[..textIndex]}{updateText}";
                    _previousEnd = updateDuration;
                    var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                    var mappedOffset = map.Map(textIndex);
                    var diffStartOffset = mappedOffset.HasValue
                        ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                        : 0;
                    var diffDuration = Math.Round(updateDuration - diffStartOffset, 3, MidpointRounding.AwayFromZero);
                    return new TranscriptUpdate {
                        StartOffset = _previousFinalEnd + diffStartOffset,
                        Duration = diffDuration,
                        Text = updateText,
                        TextIndex = _previousFinalLength + textIndex,
                        SpeakerId = update.SpeakerId,
                        Confidence = update.Confidence,
                        IsFinal = false
                    };
                }
            }

            var (_, diffIndex) = _previous
                .Zip(updateText)
                .Select((x, i) => (Match:x.First == x.Second, Index:i))
                .Append((Match:false, Index:_previous.Length))
                .SkipWhile(x => x.Match)
                .FirstOrDefault();
            var result = new TranscriptUpdate {
                StartOffset = _previousFinalEnd,
                Duration = updateDuration,
                Text = updateText,
                TextIndex = _previousFinalLength,
                SpeakerId = update.SpeakerId,
                Confidence = update.Confidence,
                IsFinal = false
            };

            if (diffIndex > 0 && diffIndex < updateText.Length) {
                var map = new LinearMap(new[] { 0.0, _previous.Length }, new[] { 0.0, _previousEnd });
                var mappedOffset = map.Map(diffIndex);
                var diffStartOffset = mappedOffset.HasValue
                    ? Math.Round(mappedOffset.Value, 3, MidpointRounding.AwayFromZero)
                    : 0;
                var diffDuration = Math.Round(updateDuration - diffStartOffset, 3, MidpointRounding.AwayFromZero);
                var newText = updateText[diffIndex..];
                result = new TranscriptUpdate {
                    StartOffset = _previousFinalEnd + diffStartOffset,
                    Duration = diffDuration,
                    Text = newText,
                    TextIndex = _previousFinalLength + diffIndex,
                    SpeakerId = update.SpeakerId,
                    Confidence = update.Confidence,
                    IsFinal = false
                };
            }

            _previous = updateText;
            _previousEnd = updateDuration;
            return result;
        }
    }
}
