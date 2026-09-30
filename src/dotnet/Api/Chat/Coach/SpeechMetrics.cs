namespace ActualChat.Chat;

public sealed record SpeechMetrics(double DurationSeconds, SpeechTextStats? Text, SpeechTimingStats? Timing)
{
    public double? WordsPerMinute {
        get {
            if (Text is null)
                return null;

            var seconds = Timing?.SpeechSeconds ?? DurationSeconds;
            return seconds > 0 ? Text.Words * 60d / seconds : null;
        }
    }
}
