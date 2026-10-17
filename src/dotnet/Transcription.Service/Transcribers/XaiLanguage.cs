namespace ActualChat.Transcription;

// xAI takes ISO-639-1 codes (plus "fil"), which Language.IsoCode already is.

public static class XaiLanguage
{
    public static ApiSet<Language> Supported { get; } = new([
        Languages.Czech,
        Languages.Danish,
        Languages.Dutch,
        Languages.English,
        Languages.EnglishIN,
        Languages.EnglishUK,
        Languages.Filipino,
        Languages.French,
        Languages.FrenchCA,
        Languages.German,
        Languages.Hindi,
        Languages.Indonesian,
        Languages.Italian,
        Languages.Japanese,
        Languages.Korean,
        Languages.Malay,
        Languages.Polish,
        Languages.Portuguese,
        Languages.PortugueseBR,
        Languages.Russian,
        Languages.Spanish,
        Languages.SpanishMX,
        Languages.SpanishUS,
        Languages.Swedish,
        Languages.Thai,
        Languages.Turkish,
        Languages.Vietnamese,
    ]);

    public static string ToXai(this Language language)
        => language.IsoCode.ToLowerInvariant();
}
