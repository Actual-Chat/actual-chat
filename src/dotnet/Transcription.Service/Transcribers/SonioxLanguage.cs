namespace ActualChat.Transcription;

// stt-rt-v5 and stt-async-v5 both cover the same 60 languages, which includes everything we
// support except the one below - verified against GET https://api.soniox.com/v1/models.

public static class SonioxLanguage
{
    private static readonly ApiSet<Language> Unsupported = new([
        Languages.Uzbek,
    ]);

    // Soniox rejects "fil" and "cnr": Filipino is the standard register of Tagalog ("tl"),
    // and Montenegrin is transcribed as Serbian ("sr"), whose Latin script it shares.
    private static readonly Dictionary<Language, string> CodeOverrides = new() {
        { Languages.Filipino, "tl" },
        { Languages.Montenegrin, "sr" },
    };
    public static ApiSet<Language> Supported { get; }
        = new(Languages.AllTranscription.Where(x => !Unsupported.Contains(x)));
    public static string ToSoniox(this Language language)
        => CodeOverrides.GetValueOrDefault(language) ?? language.IsoCode;

    public static Language? FromSoniox(string? code, IReadOnlyList<Language> expectedLanguages)
    {
        // Soniox tags tokens with the code it was sent, so an override language comes back as "sr"
        // or "tl" and must be told apart from Serbian and Tagalog by the languages the speech is expected in.
        var language = expectedLanguages.FirstOrDefault(x => CodeOverrides.GetValueOrDefault(x) == code)
            ?? Language.TryParse(code, true);
        return language is not null && language.Support.HasFlag(LanguageSupport.Transcription) ? language : null;
    }
}
