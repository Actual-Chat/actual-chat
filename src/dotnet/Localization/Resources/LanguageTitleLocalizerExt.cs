using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Names a <see cref="Language"/> in the reader's language: <see cref="Language.Title"/> is English, and ICU
/// can't name it under InvariantGlobalization, so the names are the catalog's <c>Language_*</c> keys.
/// </summary>
public static class LanguageTitleLocalizerExt
{
    private static readonly Dictionary<Language, Func<IStringLocalizer, string>> TitleGetters = new() {
        [Languages.Bosnian] = l => l.Language_Bosnian,
        [Languages.Bulgarian] = l => l.Language_Bulgarian,
        [Languages.Chinese] = l => l.Language_Chinese,
        [Languages.ChineseTW] = l => l.Language_ChineseTW,
        [Languages.Croatian] = l => l.Language_Croatian,
        [Languages.Czech] = l => l.Language_Czech,
        [Languages.Danish] = l => l.Language_Danish,
        [Languages.Dutch] = l => l.Language_Dutch,
        [Languages.English] = l => l.Language_English,
        [Languages.EnglishIN] = l => l.Language_EnglishIN,
        [Languages.EnglishUK] = l => l.Language_EnglishUK,
        [Languages.Filipino] = l => l.Language_Filipino,
        [Languages.Finnish] = l => l.Language_Finnish,
        [Languages.French] = l => l.Language_French,
        [Languages.FrenchCA] = l => l.Language_FrenchCA,
        [Languages.German] = l => l.Language_German,
        [Languages.Hindi] = l => l.Language_Hindi,
        [Languages.Indonesian] = l => l.Language_Indonesian,
        [Languages.Italian] = l => l.Language_Italian,
        [Languages.Japanese] = l => l.Language_Japanese,
        [Languages.Kazakh] = l => l.Language_Kazakh,
        [Languages.Korean] = l => l.Language_Korean,
        [Languages.Malay] = l => l.Language_Malay,
        [Languages.Marathi] = l => l.Language_Marathi,
        [Languages.Montenegrin] = l => l.Language_Montenegrin,
        [Languages.Polish] = l => l.Language_Polish,
        [Languages.Portuguese] = l => l.Language_Portuguese,
        [Languages.PortugueseBR] = l => l.Language_PortugueseBR,
        [Languages.Punjabi] = l => l.Language_Punjabi,
        [Languages.Russian] = l => l.Language_Russian,
        [Languages.Serbian] = l => l.Language_Serbian,
        [Languages.Spanish] = l => l.Language_Spanish,
        [Languages.SpanishMX] = l => l.Language_SpanishMX,
        [Languages.SpanishUS] = l => l.Language_SpanishUS,
        [Languages.Swedish] = l => l.Language_Swedish,
        [Languages.Tagalog] = l => l.Language_Tagalog,
        [Languages.Tamil] = l => l.Language_Tamil,
        [Languages.Thai] = l => l.Language_Thai,
        [Languages.Turkish] = l => l.Language_Turkish,
        [Languages.Ukrainian] = l => l.Language_Ukrainian,
        [Languages.Urdu] = l => l.Language_Urdu,
        [Languages.Uzbek] = l => l.Language_Uzbek,
        [Languages.Vietnamese] = l => l.Language_Vietnamese,
    };

    extension(IStringLocalizer l)
    {
        public string GetLanguageTitle(Language language)
            => TitleGetters.TryGetValue(language, out var getTitle) ? getTitle(l) : language.Title;
    }
}
