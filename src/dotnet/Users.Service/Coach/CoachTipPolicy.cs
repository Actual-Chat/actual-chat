using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users.Coach;

/// <summary>
/// Decides whether the record just logged earns a live tip: a word count crossing a step today
/// wins over pace, and nothing fires inside the user's tip interval.
/// </summary>
public static class CoachTipPolicy
{
    public static UserCoachTip? Evaluate(
        CoachRecord record,
        CoachDay before,
        CoachDay after,
        ApiArray<SpeechSpan> spansWithSynonyms,
        UserCoachTip previous,
        UserCoachSettings settings,
        CoachScoringSettings s,
        Moment now,
        string? language)
    {
        if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true } || record.Entry is not { } entry)
            return null;
        if (previous.LastTipAt != default && now - previous.LastTipAt < settings.TipInterval)
            return null;

        var tip = WordTip(before, after, spansWithSynonyms, s.TipWordStep) ?? PaceTip(entry, s);
        if (tip is null)
            return null;

        return tip with {
            ChatId = record.ChatId,
            EntryLid = entry.EntryLid,
            ShownAt = now,
            LastTipAt = now,
            IsDismissed = false,
        };
    }

    // Private methods

    private static UserCoachTip? WordTip(CoachDay before, CoachDay after, ApiArray<SpeechSpan> spans, int step)
    {
        if (CrossedStep(before.FillerCounts, after.FillerCounts, step) is { } filler)
            return new UserCoachTip { Kind = CoachTipKind.Filler, Word = filler.Word, Count = filler.Count };

        if (CrossedStep(before.WeakWordCounts, after.WeakWordCounts, step) is { } weak) {
            var synonyms = Synonyms(spans, weak.Word);
            if (synonyms.Count > 0)
                return new UserCoachTip {
                    Kind = CoachTipKind.WeakWord, Word = weak.Word, Count = weak.Count, Synonyms = synonyms,
                };
        }
        return null;
    }

    private static (string Word, int Count)? CrossedStep(
        ApiMap<string, int> before, ApiMap<string, int> after, int step)
    {
        foreach (var (word, count) in after.OrderByDescending(x => x.Value)) {
            var previous = before.GetValueOrDefault(word);
            if (count / step > previous / step)
                return (word, count);
        }
        return null;
    }

    private static ApiArray<string> Synonyms(ApiArray<SpeechSpan> spans, string word)
        => spans
            .FirstOrDefault(sp => sp.Kind == SpeechSpanKind.Weak && sp.Word == word && sp.Synonyms.Count > 0)
            ?.Synonyms ?? ApiArray<string>.Empty;

    private static UserCoachTip? PaceTip(CoachEntryRecord entry, CoachScoringSettings s)
    {
        if (entry.Words is not { } words || words < s.TipMinWords)
            return null;

        var seconds = entry.SpeechSeconds ?? entry.DurationSeconds;
        if (seconds <= 0)
            return null;

        var wpm = (int)Math.Round(words * 60 / seconds);
        if (wpm > s.TipPaceFastWpm)
            return new UserCoachTip { Kind = CoachTipKind.SlowDown, Wpm = wpm };
        if (wpm < s.TipPaceSlowWpm)
            return new UserCoachTip { Kind = CoachTipKind.SpeedUp, Wpm = wpm };

        return null;
    }
}
