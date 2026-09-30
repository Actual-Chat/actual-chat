namespace ActualChat.Chat;

/// <summary>
/// Speech time and pauses of one transcript, from the word boundaries of its time map.
/// </summary>
public sealed record SpeechTimingStats(double SpeechSeconds, int Pauses, double PauseSeconds)
{
    public static SpeechTimingStats? Compute(PlayableTextMarkup markup, double durationSeconds, double minPauseSeconds)
    {
        var timeMap = markup.TimeMap;
        if (timeMap.IsDegenerate || durationSeconds <= 0)
            return null;

        var pauses = 0;
        var pauseSeconds = 0d;
        double? previousEnd = null;
        foreach (var word in markup.Words) {
            // The word regex keeps the trailing whitespace inside the word, so its range end maps to
            // the next word's start; the pause lives between the trimmed end and that next start.
            var textEnd = word.TextRange.End;
            while (textEnd > word.TextRange.Start && char.IsWhiteSpace(word.Value[textEnd - word.TextRange.Start - 1]))
                textEnd--;
            if (textEnd == word.TextRange.Start)
                continue;

            var start = timeMap.TryMap(word.TextRange.Start);
            var end = timeMap.TryMap(textEnd);
            if (start is null || end is null || end < start)
                return null;

            if (previousEnd is { } pe) {
                var gap = start.Value - pe;
                if (gap < -0.001)
                    return null;
                if (gap >= minPauseSeconds) {
                    pauses++;
                    pauseSeconds += gap;
                }
            }
            previousEnd = end;
        }
        return new SpeechTimingStats(Math.Max(0, durationSeconds - pauseSeconds), pauses, pauseSeconds);
    }
}
