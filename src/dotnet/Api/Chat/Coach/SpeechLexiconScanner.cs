using System.Globalization;

namespace ActualChat.Chat;

/// <summary>
/// Marks a transcript that keeps growing: each call rescans only the text that changed since the previous
/// one, so marking a whole utterance costs its length and not its length squared.
/// </summary>
public sealed class SpeechLexiconScanner(SpeechLexicon lexicon)
{
    // A pattern of a script without word spaces is assumed to be no longer than this
    private const int UnspacedMargin = 32;

    private readonly Dictionary<string, List<SpeechSpan>> _spans = new();
    private string _text = "";

    // The spans of an earlier language win where two overlap
    public ApiArray<SpeechSpan> Scan(string text, IReadOnlyList<Language> languages)
    {
        var common = _text.AsSpan().CommonPrefixLength(text);
        var result = ApiArray<SpeechSpan>.Empty;
        var scanned = new HashSet<string>();
        foreach (var language in languages) {
            var iso = language.IsoCode;
            if (!scanned.Add(iso))
                continue;

            var spans = Rescan(text, language, common, _spans.GetValueOrDefault(iso));
            _spans[iso] = spans;
            result = result.AddNonOverlapping(spans);
        }
        foreach (var iso in _spans.Keys.Where(k => !scanned.Contains(k)).ToList())
            _spans.Remove(iso);
        _text = text;
        return result;
    }

    // Private methods

    private List<SpeechSpan> Rescan(string text, Language language, int common, List<SpeechSpan>? previous)
    {
        if (previous is null)
            return lexicon.FindSpans(text, language).ToList();

        var from = lexicon.IsWholeWord(language)
            ? WordStart(text, common)
            : Math.Max(0, common - UnspacedMargin);
        var spans = new List<SpeechSpan>();
        foreach (var span in previous) {
            if (span.Start + span.Length > from) {
                from = Math.Min(from, span.Start);
                break;
            }
            spans.Add(span);
        }
        spans.AddRange(lexicon.FindSpans(text, language, from));
        return spans;
    }

    // The start of the word the position is in, or the position itself between words: the last word of
    // the unchanged part may still be growing
    private static int WordStart(string text, int position)
    {
        var i = Math.Min(position, text.Length);
        while (i > 0 && IsWordChar(text[i - 1]))
            i--;
        return i;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c)
            || c is '-' or '\'' or '’'
            || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark;
}
