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
