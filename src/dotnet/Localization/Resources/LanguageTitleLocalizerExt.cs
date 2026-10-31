using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Names a <see cref="Language"/> in the reader's language: <see cref="Language.Title"/> is English, and ICU
/// can't name it under InvariantGlobalization, so the names are the catalog's <c>Language_*</c> keys.
/// </summary>
public static class LanguageTitleLocalizerExt
{
    private static readonly Dictionary<Language, Func<IStringLocalizer, string>> TitleGetters = new() {
        [Languages.Afrikaans] = l => l.Language_Afrikaans,
        [Languages.Albanian] = l => l.Language_Albanian,
        [Languages.Arabic] = l => l.Language_Arabic,
        [Languages.Azerbaijani] = l => l.Language_Azerbaijani,
        [Languages.Basque] = l => l.Language_Basque,
        [Languages.Belarusian] = l => l.Language_Belarusian,
        [Languages.Bengali] = l => l.Language_Bengali,
        [Languages.Bosnian] = l => l.Language_Bosnian,
        [Languages.Bulgarian] = l => l.Language_Bulgarian,
        [Languages.Catalan] = l => l.Language_Catalan,
        [Languages.Chinese] = l => l.Language_Chinese,
        [Languages.ChineseTW] = l => l.Language_ChineseTW,
        [Languages.Croatian] = l => l.Language_Croatian,
        [Languages.Czech] = l => l.Language_Czech,
        [Languages.Danish] = l => l.Language_Danish,
        [Languages.Dutch] = l => l.Language_Dutch,
        [Languages.English] = l => l.Language_English,
        [Languages.EnglishIN] = l => l.Language_EnglishIN,
        [Languages.EnglishUK] = l => l.Language_EnglishUK,
        [Languages.Estonian] = l => l.Language_Estonian,
        [Languages.Filipino] = l => l.Language_Filipino,
        [Languages.Finnish] = l => l.Language_Finnish,
        [Languages.French] = l => l.Language_French,
        [Languages.FrenchCA] = l => l.Language_FrenchCA,
        [Languages.Galician] = l => l.Language_Galician,
        [Languages.German] = l => l.Language_German,
        [Languages.Greek] = l => l.Language_Greek,
        [Languages.Gujarati] = l => l.Language_Gujarati,
        [Languages.Hebrew] = l => l.Language_Hebrew,
        [Languages.Hindi] = l => l.Language_Hindi,
        [Languages.Hungarian] = l => l.Language_Hungarian,
        [Languages.Indonesian] = l => l.Language_Indonesian,
        [Languages.Italian] = l => l.Language_Italian,
        [Languages.Japanese] = l => l.Language_Japanese,
        [Languages.Kannada] = l => l.Language_Kannada,
        [Languages.Kazakh] = l => l.Language_Kazakh,
        [Languages.Korean] = l => l.Language_Korean,
        [Languages.Latvian] = l => l.Language_Latvian,
        [Languages.Lithuanian] = l => l.Language_Lithuanian,
        [Languages.Macedonian] = l => l.Language_Macedonian,
        [Languages.Malay] = l => l.Language_Malay,
        [Languages.Malayalam] = l => l.Language_Malayalam,
        [Languages.Marathi] = l => l.Language_Marathi,
        [Languages.Montenegrin] = l => l.Language_Montenegrin,
        [Languages.Norwegian] = l => l.Language_Norwegian,
        [Languages.Polish] = l => l.Language_Polish,
        [Languages.Portuguese] = l => l.Language_Portuguese,
        [Languages.PortugueseBR] = l => l.Language_PortugueseBR,
        [Languages.Punjabi] = l => l.Language_Punjabi,
        [Languages.Romanian] = l => l.Language_Romanian,
        [Languages.Russian] = l => l.Language_Russian,
        [Languages.Serbian] = l => l.Language_Serbian,
        [Languages.Slovak] = l => l.Language_Slovak,
        [Languages.Slovenian] = l => l.Language_Slovenian,
        [Languages.Spanish] = l => l.Language_Spanish,
        [Languages.SpanishMX] = l => l.Language_SpanishMX,
        [Languages.SpanishUS] = l => l.Language_SpanishUS,
        [Languages.Swahili] = l => l.Language_Swahili,
        [Languages.Swedish] = l => l.Language_Swedish,
        [Languages.Tagalog] = l => l.Language_Tagalog,
        [Languages.Tamil] = l => l.Language_Tamil,
        [Languages.Telugu] = l => l.Language_Telugu,
        [Languages.Thai] = l => l.Language_Thai,
        [Languages.Turkish] = l => l.Language_Turkish,
        [Languages.Ukrainian] = l => l.Language_Ukrainian,
        [Languages.Urdu] = l => l.Language_Urdu,
        [Languages.Uzbek] = l => l.Language_Uzbek,
        [Languages.Vietnamese] = l => l.Language_Vietnamese,
        [Languages.Welsh] = l => l.Language_Welsh,
    };

    extension(IStringLocalizer l)
    {
        public string GetLanguageTitle(Language language)
            => TitleGetters.TryGetValue(language, out var getTitle) ? getTitle(l) : language.Title;
    }
}
