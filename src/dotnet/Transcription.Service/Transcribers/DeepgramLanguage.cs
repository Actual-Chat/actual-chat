namespace ActualChat.Transcription;

public static class DeepgramLanguage
{
    private static readonly Dictionary<Language, string> Map = new () {
        { Languages.English, "en" },
        { Languages.EnglishUK, "en" },
        { Languages.EnglishIN, "en" },
        { Languages.French, "fr" },
        { Languages.FrenchCA, "fr-CA" },
        { Languages.German, "de" },
        { Languages.Hindi, "hi" },
        { Languages.PortugueseBR, "pt-BR" },
        { Languages.Portuguese, "pt" },
        { Languages.Spanish, "es" },
        { Languages.SpanishMX, "es-419" },
        { Languages.SpanishUS, "es-419" },
        { Languages.Russian, "ru" },
        { Languages.Chinese, "zh-CN" },
        { Languages.ChineseTW, "zh-TW" },
        { Languages.Japanese, "ja" },
        { Languages.Korean, "ko" },
        { Languages.Italian, "it" },
        { Languages.Dutch, "nl" },
        { Languages.Ukrainian, "uk" },
        { Languages.Czech, "cs" },
        { Languages.Swedish, "sv" },
        { Languages.Danish, "da" },
        { Languages.Finnish, "fi" },
        { Languages.Polish, "pl" },
        { Languages.Turkish, "tr" },
        { Languages.Vietnamese, "vi" },
        { Languages.Thai, "th" },
    };

    private static readonly Dictionary<string, Language> InvertedMap = Map
        .GroupBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            g => g.Key,
            g => g.First().Key,
            StringComparer.OrdinalIgnoreCase);

    public static ApiSet<Language> Supported { get; } = new(Map.Keys);

    public static string ToDeepgram(this Language language)
        => Map.TryGetValue(language, out var deepgramLanguage)
            ? deepgramLanguage
            : throw StandardError.NotSupported<DeepgramTranscriber>(
                $"Language '{language.Id}' is not supported");

    public static Language FromDeepgram(string deepgramLanguage)
        => InvertedMap.TryGetValue(deepgramLanguage, out var language)
            ? language
            : throw StandardError.NotSupported<DeepgramTranscriber>(
                $"Deepgram Language '{deepgramLanguage}' is not supported");
}
