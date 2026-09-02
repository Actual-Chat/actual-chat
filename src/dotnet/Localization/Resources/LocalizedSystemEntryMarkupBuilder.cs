using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Words system entries in the reader's language. The UI resolves one from the circuit; code
/// with no reader takes one per language.
/// </summary>
public sealed class LocalizedSystemEntryMarkupBuilder(IStringLocalizer l) : SystemEntryMarkupBuilder
{
    private static readonly ConcurrentDictionary<Language, LocalizedSystemEntryMarkupBuilder> Cache = new();

    protected override string SomeoneName => l.SystemEntry_Someone;
    protected override string MemberJoined => l.SystemEntry_MemberJoined;
    protected override string MemberLeft => l.SystemEntry_MemberLeft;
    protected override string AttentionRequested => l.SystemEntry_AttentionRequested;

    // Keyed by language rather than by localizer: a circuit's localizer is scoped, and holding one
    // here would pin every circuit that ever rendered. The UI builds its own through DI instead.
    public static LocalizedSystemEntryMarkupBuilder Get(Language language)
        => Cache.GetOrAdd(language,
            static x => new LocalizedSystemEntryMarkupBuilder(LanguageStringLocalizer.Get(x)));
}
