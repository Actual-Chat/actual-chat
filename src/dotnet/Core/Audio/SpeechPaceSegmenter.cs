namespace ActualChat.Audio;

public static class SpeechPaceSegmenter
{
    public sealed record Options
    {
        public static readonly Options Default = new();

        public int TargetMilliseconds { get; init; } = 10_000;
        public int MinMilliseconds { get; init; } = 3_000;
        public int MinWords { get; init; } = 5;
        public int PauseMilliseconds { get; init; } = 1_000;
    }

    public static SpeechPaceAnalysis Compute(
        ReadOnlySpan<TimedSpeechWord> words, int recordingMilliseconds, Options? options = null)
    {
        options ??= Options.Default;
        if (recordingMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(recordingMilliseconds));

        if (options.TargetMilliseconds <= 0 || options.TargetMilliseconds > int.MaxValue / 2
            || options.MinMilliseconds <= 0 || options.MinMilliseconds > options.TargetMilliseconds
            || options.MinWords <= 0 || options.PauseMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        var segments = new List<SpeechPaceSegment>();
        var blockStart = -1;
        var previousEnd = 0;
        var previousTextEnd = 0;
        var validWords = 0;
        var rejectedWords = 0;
        var knownMilliseconds = 0;
        var unclassifiedWords = 0;
        var unclassifiedMilliseconds = 0;
        var pauseMilliseconds = 0;
        for (var i = 0; i <= words.Length; i++) {
            var isEnd = i == words.Length;
            var word = isEnd ? default : words[i];
            var isValid = !isEnd && word.TimeRange.Start >= previousEnd
                && word.TimeRange.End > word.TimeRange.Start && word.TimeRange.End <= recordingMilliseconds
                && word.TextRange.Start >= previousTextEnd && word.TextRange.End > word.TextRange.Start;
            var gap = isValid && blockStart >= 0 ? word.TimeRange.Start - previousEnd : 0;
            if (blockStart >= 0 && (!isValid || gap >= options.PauseMilliseconds)) {
                var block = words[blockStart..i];
                knownMilliseconds += block[^1].TimeRange.End - block[0].TimeRange.Start;
                var uncovered = AddBlock(block, options, segments);
                unclassifiedWords += uncovered.Words;
                unclassifiedMilliseconds += uncovered.Milliseconds;
                pauseMilliseconds += gap;
                blockStart = -1;
            }
            if (isEnd)
                break;

            if (!isValid) {
                rejectedWords++;
                continue;
            }

            if (blockStart < 0)
                blockStart = i;
            previousEnd = word.TimeRange.End;
            previousTextEnd = word.TextRange.End;
            validWords++;
        }
        return new SpeechPaceAnalysis(
            segments.ToArray(), validWords, rejectedWords, unclassifiedWords,
            unclassifiedMilliseconds, pauseMilliseconds,
            recordingMilliseconds - knownMilliseconds - pauseMilliseconds);
    }

    // Private methods

    private static (int Words, int Milliseconds) AddBlock(
        ReadOnlySpan<TimedSpeechWord> words, Options options, List<SpeechPaceSegment> segments)
    {
        var firstSegment = segments.Count;
        var start = 0;
        for (var i = 0; i < words.Length; i++) {
            var segmentStart = start == 0 ? words[0].TimeRange.Start : words[start - 1].TimeRange.End;
            var duration = words[i].TimeRange.End - segmentStart;
            var count = i - start + 1;
            if (duration < options.TargetMilliseconds || count < options.MinWords)
                continue;

            segments.Add(new SpeechPaceSegment(
                (words[start].TextRange.Start, words[i].TextRange.End),
                (segmentStart, words[i].TimeRange.End), count));
            start = i + 1;
        }
        if (start == words.Length)
            return (0, 0);

        var tailStart = start == 0 ? words[0].TimeRange.Start : words[start - 1].TimeRange.End;
        var tail = new SpeechPaceSegment(
            (words[start].TextRange.Start, words[^1].TextRange.End),
            (tailStart, words[^1].TimeRange.End), words.Length - start);
        if (tail.Words >= options.MinWords && tail.DurationMilliseconds >= options.MinMilliseconds) {
            segments.Add(tail);
            return (0, 0);
        }

        if (segments.Count > firstSegment) {
            var previous = segments[^1];
            var duration = tail.TimeRange.End - previous.TimeRange.Start;
            if (duration <= 2 * options.TargetMilliseconds) {
                segments[^1] = new SpeechPaceSegment(
                    (previous.TextRange.Start, tail.TextRange.End),
                    (previous.TimeRange.Start, tail.TimeRange.End), previous.Words + tail.Words);
                return (0, 0);
            }
        }
        return (tail.Words, tail.DurationMilliseconds);
    }
}
