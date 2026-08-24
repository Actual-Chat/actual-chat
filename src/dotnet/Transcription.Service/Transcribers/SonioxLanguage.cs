namespace ActualChat.Transcription;

// stt-rt-v5 and stt-async-v5 both cover the same 60 languages, which includes everything we
// support except the one below - verified against GET https://api.soniox.com/v1/models.

public static class SonioxLanguage
{
    private static readonly ApiSet<Language> Unsupported = new([
        Languages.Uzbek,
    ]);
    // Filipino is the standardized register of Tagalog and Soniox exposes only "tl",
    // so "fil" would be rejected despite the audio being well within the model's reach.
    private static readonly Dictionary<Language, string> CodeOverrides = new() {
        { Languages.Filipino, "tl" },
    };
    public static ApiSet<Language> Supported { get; }
        = new(Languages.AllTranscription.Where(x => !Unsupported.Contains(x)));
    public static string ToSoniox(this Language language)
        => CodeOverrides.GetValueOrDefault(language) ?? language.IsoCode;
}
