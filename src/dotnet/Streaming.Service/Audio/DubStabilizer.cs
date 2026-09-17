using System.Text.RegularExpressions;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public enum DubDecision
{
    Undecided,
    Dub,
    NoDub,
}

/// <summary>
/// Turns a translated transcript stream into text a TTS engine may speak - only the stable text,
/// which arrives clause by clause, and only what wasn't sent yet - and decides whether the source
/// needs dubbing at all.
/// </summary>
public sealed partial class DubStabilizer
{
    public const int MinDecisionLength = 10;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();

    public string SentText { get; private set; } = "";

    public void Skip(Transcript translated, float backlogEnd)
    {
        // Whatever is spoken next starts after the backlog a late listener must not hear. The
        // translator puts a time map point at every clause end, so the backlog ends at the last one
        // at or before backlogEnd - the clause the join point falls in is spoken whole - whether the
        // transcript is one clause or a late reader's fold of everything translated so far
        var text = translated.Text;
        var end = 0;
        foreach (var point in translated.TimeMap.Points) {
            if (point.Y > backlogEnd + Transcript.TimeMapEpsilon.Y)
                break;

            end = Math.Min(text.Length, (int)MathF.Round(point.X));
        }
        if (end > GetSentPrefixLength(text))
            SentText = text[..end];
    }

    public string? Next(Transcript translated)
    {
        // The translator hands over whole clauses, sized so Soniox TTS speaks each at once;
        // there's nothing to cut here any more - only what's stable and wasn't sent yet
        if (!translated.IsStable)
            return null;

        var text = translated.Text;
        return Send(text, GetSentPrefixLength(text), text.Length);
    }

    public static DubDecision Decide(Transcript source, Transcript translated, Language targetLanguage)
    {
        // NoDub only when every language heard is the target: a needless dub is the cheaper error, a
        // wrong NoDub loses the utterance for the listener
        if (source.Languages.Length > 0 && source.Text.Length >= MinDecisionLength)
            return source.Languages.All(x => x.IsoCode == targetLanguage.IsoCode)
                ? DubDecision.NoDub
                : DubDecision.Dub;
        if (!translated.IsStable)
            return DubDecision.Undecided;

        var translatedText = Normalize(translated.Text);
        if (translatedText.Length < MinDecisionLength)
            return DubDecision.Undecided;

        // The translator hands the source text back verbatim when no translation is needed
        return Normalize(source.Text).StartsWith(translatedText)
            ? DubDecision.NoDub
            : DubDecision.Dub;
    }

    // Private methods

    private int GetSentPrefixLength(string text)
        => text.StartsWith(SentText) ? SentText.Length : text.GetCommonPrefixLength(SentText);

    private string? Send(string text, int prefixLength, int end)
    {
        var chunk = text[prefixLength..end];
        if (chunk.IsNullOrWhiteSpace())
            return null;

        SentText = text[..end];
        return chunk;
    }

    private static string Normalize(string text)
        => WhitespaceRegex.Replace(text, " ").Trim().ToLower();
}
