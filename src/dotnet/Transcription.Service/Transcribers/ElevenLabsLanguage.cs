namespace ActualChat.Transcription;

// Scribe v2 takes ISO-639-1 or ISO-639-3, which Language.IsoCode already is, so no mapping table
// is needed. Batch covers 99 languages - everything we support; realtime covers 90+ and doesn't
// enumerate them, so the streaming set stays undeclared rather than guessed.

public static class ElevenLabsLanguage
{
    public static string ToElevenLabs(this Language language)
        => language.IsoCode;
}
