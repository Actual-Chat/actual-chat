namespace ActualChat;

/// <summary>
/// What a <see cref="Language"/> is usable for: most declared languages are transcription-only,
/// held-back ones are Preview alone, and the Max pseudo-locale is none of these.
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
    // Held back as a whole, so never combined with other flags: the catalog ships, but nothing offers or emits it.
    // Becomes All once apps throwing on its code are rare; meanwhile it renders when set elsewhere or via ?lang=.
    Preview = 4,
    All = Transcription | UI,
}
