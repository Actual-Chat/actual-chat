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
/// Turns a translated transcript stream into text a TTS engine may speak - only the stable prefix,
/// only what wasn't sent yet - and decides whether the source needs dubbing at all.
/// </summary>
public sealed partial class DubStabilizer
{
    private const int MinDecisionLength = 10;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();

    public string SentText { get; private set; } = "";

    public string? Next(Transcript translated)
    {
        if (!translated.IsStable)
            return null;

        var text = translated.Text;
        var prefixLength = text.StartsWith(SentText) ? SentText.Length : CommonPrefixLength(text, SentText);
        var chunk = text[prefixLength..];
        if (chunk.IsNullOrWhiteSpace())
            return null;

        SentText = text;
        return chunk;
    }

    public static DubDecision Decide(Transcript source, Transcript translated, Language targetLanguage)
    {
        if (source.Languages.Length > 0 && source.Text.Length >= MinDecisionLength)
            return source.Languages.Any(x => x.IsoCode == targetLanguage.IsoCode)
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

    private static string Normalize(string text)
        => WhitespaceRegex.Replace(text, " ").Trim().ToLower();

    private static int CommonPrefixLength(string x, string y)
    {
        var length = Math.Min(x.Length, y.Length);
        var i = 0;
        while (i < length && x[i] == y[i])
            i++;
        return i;
    }
}
