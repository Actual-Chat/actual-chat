namespace ActualChat.Transcription;

/// <summary>
/// How fast a synthesized voice speaks, in characters per second. A character is an artefact of
/// orthography rather than of speech, so this is per language: the same sentence is far fewer
/// characters in a character-dense script, and reading one at another's rate is wrong by threefold.
/// </summary>
public static class SpeechRate
{
    // Measured through the synthesizer, two passages per language, stable across both. A language
    // that has not been measured falls back to the English rate, which over-estimates how long a
    // character takes in a dense script - erring towards speaking more of a message, never less.
    private const double DefaultCharsPerSecond = 15;
    private static readonly Dictionary<string, double> CharsPerSecondByCode = new(StringComparer.Ordinal) {
        { Languages.English.IsoCode, 15 },
        { Languages.Russian.IsoCode, 17 },
        { Languages.Japanese.IsoCode, 6 },
        { Languages.Chinese.IsoCode, 4.3 },
    };

    public static double CharsPerSecond(Language language)
        => CharsPerSecondByCode.GetValueOrDefault(language.IsoCode, DefaultCharsPerSecond);

    public static int ToCharCount(Language language, TimeSpan duration, double speed = 1)
        => (int)Math.Round(duration.TotalSeconds * CharsPerSecond(language) * speed);
}
