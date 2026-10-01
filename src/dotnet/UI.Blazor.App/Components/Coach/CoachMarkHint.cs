using ActualChat.Chat;
using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// The popover text behind a marked word, and the round trip of a span through menu arguments.
/// </summary>
public sealed class CoachMarkHint(IStringLocalizer l)
{
    private const char SynonymSeparator = '\n';

    public sealed record Hint(string Title, string Body, ApiArray<string> Synonyms);
    public sealed record Context(string Before, string Word, string After);

    public static Context? GetContext(string text, SpeechSpan span)
    {
        if (span.Start < 0 || span.Length <= 0 || span.Start > text.Length
            || span.Length > text.Length - span.Start)
            return null;

        var word = text.Substring(span.Start, span.Length);
        if (!word.Equals(span.Word, StringComparison.OrdinalIgnoreCase))
            return null;

        var start = span.Start;
        var end = span.Start + span.Length;
        while (start > 0 && span.Start - start < 100 && !IsBoundary(text[start - 1]))
            start--;
        while (end < text.Length && end - span.Start - span.Length < 100 && !IsBoundary(text[end]))
            end++;
        if (start > 0 && char.IsLowSurrogate(text[start]))
            start++;
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]))
            end--;

        var before = text[start..span.Start].TrimStart();
        var after = text[(span.Start + span.Length)..end].TrimEnd();
        if (start > 0 && !IsBoundary(text[start - 1]))
            before = "…" + before;
        if (end < text.Length)
            after += IsBoundary(text[end]) ? text[end].ToString() : "…";

        return new Context(before, word, after);

        static bool IsBoundary(char c) => c is '.' or '!' or '?' or '。' or '！' or '？' or '\n' or '\r';
    }

    public Hint For(SpeechSpan span)
        => span.Kind switch {
            SpeechSpanKind.Weak => new Hint(
                l.Coach_TipWeakWordTitle, l.Coach_TipWeakWordBody_Format(span.Word), span.Synonyms),
            SpeechSpanKind.Profanity => new Hint(
                l.Coach_MarkProfanityTitle, l.Coach_MarkProfanityBody_Format(span.Word), span.Synonyms),
            SpeechSpanKind.Repetition => new Hint(
                l.Coach_MarkRepetitionTitle, l.Coach_MarkRepetitionBody_Format(span.Word), ApiArray<string>.Empty),
            _ => new Hint(l.Coach_TipFillerTitle, l.Coach_MarkFillerBody_Format(span.Word), ApiArray<string>.Empty),
        };

    public static string[] ToArguments(SpeechSpan span)
        => [((int)span.Kind).ToString(), span.Word, string.Join(SynonymSeparator, span.Synonyms)];

    public static SpeechSpan FromArguments(string[] arguments)
    {
        if (arguments.Length < 3)
            throw new ArgumentOutOfRangeException(nameof(arguments));

        var kind = (SpeechSpanKind)int.Parse(arguments[0]);
        var synonyms = arguments[2].IsNullOrEmpty()
            ? ApiArray<string>.Empty
            : arguments[2].Split(SynonymSeparator).ToApiArray();
        return new SpeechSpan(kind, arguments[1], 0, arguments[1].Length, synonyms);
    }
}
