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
/// only what wasn't sent yet, only up to a clause boundary until <see cref="Flush"/> -
/// and decides whether the source needs dubbing at all.
/// </summary>
public sealed partial class DubStabilizer
{
    public const int MinDecisionLength = 10;
    public const int MaxUnpunctuatedLength = 120;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegexFactory();
    private static readonly Regex WhitespaceRegex = WhitespaceRegexFactory();
    // The ASCII marks need a following space or the end: "3.5" or "10:30" end no clause.
    // The fullwidth ones are never followed by a space and never sit inside a number.
    [GeneratedRegex(@"[.!?…,;:](?=\s|$)|[。！？，；：]")]
    private static partial Regex ClauseEndRegexFactory();
    private static readonly Regex ClauseEndRegex = ClauseEndRegexFactory();

    private string _stableText = "";

    public string SentText { get; private set; } = "";

    public void Skip(Transcript translated)
        // Whatever is spoken next starts after this text - the backlog a late listener must not hear
        => SentText = translated.Text;

    public string? Next(Transcript translated)
    {
        // Soniox speaks clause-complete text at once but holds a mid-clause fragment until more text
        // or the stream's end (measured: 0.3 s vs 4.0 s to the first audio), so the fragment after the
        // last boundary waits here for the next stable text; a long run without any boundary goes anyway.
        if (!translated.IsStable)
            return null;

        var text = translated.Text;
        _stableText = text;
        var prefixLength = GetSentPrefixLength(text);
        var end = prefixLength;
        foreach (var match in ClauseEndRegex.EnumerateMatches(text.AsSpan(prefixLength)))
            end = prefixLength + match.Index + match.Length;
        if (text.Length - end > MaxUnpunctuatedLength)
            end = text.Length;
        return Send(text, prefixLength, end);
    }

    public string? Flush()
        => Send(_stableText, GetSentPrefixLength(_stableText), _stableText.Length);

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
