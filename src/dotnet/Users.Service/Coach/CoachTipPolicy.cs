using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users;

/// <summary>
/// Decides whether the entry just logged earns a live tip, judged over the last TipWindow of the
/// user's own entries: a word said TipWordCount times in the window wins over pace, a tipped word
/// rests for TipWordCooldown, and nothing fires inside the user's tip interval.
/// </summary>
public static class CoachTipPolicy
{
    public static UserCoachTip? Evaluate(
        CoachRecord record,
        IReadOnlyList<CoachRecord> window,
        ApiArray<SpeechSpan> spansWithSynonyms,
        UserCoachTip previous,
        UserCoachSettings settings,
        CoachScoringSettings s,
        Moment now,
        string? language)
    {
        if (settings is not { IsCoachingEnabled: true, AreLiveTipsEnabled: true } || record.Entry is null)
            return null;
        if (previous.LastTipAt != default && now - previous.LastTipAt < settings.TipInterval)
            return null;
        // Re-tagged or edited old entries are not live feedback
        if (now - record.OccurredAt > s.TipWindow)
            return null;

        var entries = window
            .Where(r => r.Entry is not null && now - r.OccurredAt <= s.TipWindow)
            .Select(r => r.Entry!)
            .ToList();
        var tip = WordTip(entries, spansWithSynonyms, previous, s, now) ?? PaceTip(entries, s, language);
        if (tip is null)
            return null;

        var wordTipAt = previous.WordTipAt;
        if (!tip.Word.IsNullOrEmpty()) {
            var map = wordTipAt.ToDictionary(x => x.Key, x => x.Value);
            map[tip.Word] = now;
            wordTipAt = new ApiMap<string, Moment>(map);
        }
        return tip with {
            ChatId = record.ChatId,
            EntryLid = record.Entry.EntryLid,
            WindowMinutes = (int)s.TipWindow.TotalMinutes,
            WordTipAt = wordTipAt,
            ShownAt = now,
            LastTipAt = now,
            IsDismissed = false,
        };
    }

    // Private methods

    private static UserCoachTip? WordTip(
        List<CoachEntryRecord> entries, ApiArray<SpeechSpan> spans, UserCoachTip previous, CoachScoringSettings s, Moment now)
    {
        var filler = TopWord(entries, s, previous, now, SpeechSpanKind.Filler, SpeechSpanKind.FilledPause);
        if (filler is { } f)
            return new UserCoachTip { Kind = CoachTipKind.Filler, Word = f.Word, Count = f.Count };

        var weak = TopWord(entries, s, previous, now, SpeechSpanKind.Weak);
        if (weak is { } w) {
            var synonyms = Synonyms(spans, w.Word);
            if (synonyms.Count > 0)
                return new UserCoachTip { Kind = CoachTipKind.WeakWord, Word = w.Word, Count = w.Count, Synonyms = synonyms };
        }
        return null;
    }

    private static (string Word, int Count)? TopWord(
        List<CoachEntryRecord> entries, CoachScoringSettings s, UserCoachTip previous, Moment now,
        params SpeechSpanKind[] kinds)
    {
        var counts = new Dictionary<string, int>();
        foreach (var entry in entries)
            foreach (var span in entry.Spans)
                if (kinds.Contains(span.Kind))
                    counts[span.Word] = counts.GetValueOrDefault(span.Word) + 1;

        foreach (var (word, count) in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key)) {
            if (count < s.TipWordCount)
                return null;
            if (previous.WordTipAt.TryGetValue(word, out var tippedAt) && now - tippedAt < s.TipWordCooldown)
                continue;

            return (word, count);
        }
        return null;
    }

    private static ApiArray<string> Synonyms(ApiArray<SpeechSpan> spans, string word)
        => spans
            .FirstOrDefault(sp => sp.Kind == SpeechSpanKind.Weak && sp.Word == word && sp.Synonyms.Count > 0)
            ?.Synonyms ?? ApiArray<string>.Empty;

    private static UserCoachTip? PaceTip(List<CoachEntryRecord> entries, CoachScoringSettings s, string? language)
    {
        var words = 0;
        var seconds = 0d;
        foreach (var entry in entries) {
            if (entry.Words is not { } entryWords)
                continue;

            words += entryWords;
            seconds += entry.SpeechSeconds ?? entry.DurationSeconds;
        }
        if (words < s.TipMinWords || seconds <= 0)
            return null;

        var wpm = (int)Math.Round(words * 60 / seconds);
        var kind = wpm > s.TipPaceFastWpm ? CoachTipKind.SlowDown
            : wpm < s.TipPaceSlowWpm ? CoachTipKind.SpeedUp
            : CoachTipKind.None;
        if (kind == CoachTipKind.None)
            return null;

        var range = CoachScoring.PaceRange(s, language);
        return new UserCoachTip {
            Kind = kind,
            Wpm = wpm,
            PaceSlowWpm = (int)Math.Round(range.Slow),
            PaceFastWpm = (int)Math.Round(range.Fast),
        };
    }
}
