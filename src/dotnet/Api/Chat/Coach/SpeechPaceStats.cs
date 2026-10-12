using ActualChat.Audio;

namespace ActualChat.Chat;

public static class SpeechPaceStats
{
    public static SpeechPaceAnalysis? Compute(
        PlayableTextMarkup markup, double durationSeconds, Language? language,
        bool hasDetailedTiming, SpeechPaceSegmenter.Options? options = null)
    {
        if (!hasDetailedTiming || markup.TimeMap.IsDegenerate || markup.TimeMap.Length <= 2
            || !markup.TimeMap.IsValid() || !SpeechTextStats.IsWordSplittable(language)
            || !double.IsFinite(durationSeconds)
            || durationSeconds <= 0 || durationSeconds > int.MaxValue / 1_000d)
            return null;

        var points = Enumerable.Range(0, markup.TimeMap.Length)
            .ToDictionary(i => markup.TimeMap[i].X, i => markup.TimeMap[i].Y);
        var words = new List<TimedSpeechWord>();
        foreach (var word in markup.Words) {
            if (SpeechTextStats.GetWordRange(word) is not { } textRange)
                continue;

            var textEnd = word.TextRange.Start + word.Value.AsSpan().TrimEnd().Length;
            var timeRange = points.TryGetValue(word.TextRange.Start, out var start)
                && points.TryGetValue(textEnd, out var end)
                && float.IsFinite(start) && float.IsFinite(end)
                && start >= 0 && end >= start && end <= int.MaxValue / 1_000d
                ? new Range<int>((int)Math.Round(start * 1_000d), (int)Math.Round(end * 1_000d))
                : new Range<int>(-1, -1);
            words.Add(new TimedSpeechWord(textRange, timeRange));
        }
        return SpeechPaceSegmenter.Compute(words.ToArray(), (int)Math.Round(durationSeconds * 1_000), options);
    }
}
