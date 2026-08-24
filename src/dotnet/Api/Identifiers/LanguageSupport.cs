namespace ActualChat;

/// <summary>
/// What a <see cref="Language"/> is usable for. A language can be one, both or
/// neither: Montenegrin ships a UI catalog but no transcriber knows its "cnr"
/// code, and most declared languages are transcription-only.
/// </summary>
[Flags]
public enum LanguageSupport
{
    None = 0,
    // Offered as a spoken language and accepted by at least one transcriber.
    Transcription = 1,
    // Ships Strings.<IsoCode>.json + Messages.<IsoCode>.json, so the app can render in it.
    // Only the canonical variant carries this: en-GB and en-IN resolve to the same "en" catalog.
    UI = 2,
    All = Transcription | UI,
}
