using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.Maui;

/// <summary>
/// The <see cref="IStringLocalizer"/> for code with no Blazor circuit - native dialogs, local
/// notifications, the Live Activity, the iOS share extension. Resolved against the language the
/// app mirrors into <see cref="MauiPreferences.UILanguage"/> - or, until the app has run once
/// since that key appeared, against the device's own language ordering.
/// </summary>
public static class AppStrings
{
    public static IStringLocalizer L => LanguageStringLocalizer.Get(UILanguage);
    private static Language UILanguage
        => MauiPreferences.UILanguage ?? Languages.DetectUILanguage(DeviceLanguages);

    private static IReadOnlyList<string> DeviceLanguages {
        get {
#if ANDROID
            var locales = Android.OS.LocaleList.Default;
            var result = new string[locales.Size()];
            for (var i = 0; i < result.Length; i++)
                result[i] = locales.Get(i)!.ToLanguageTag();
            return result;
#elif IOS || MACCATALYST
            return Foundation.NSLocale.PreferredLanguages;
#else
            return [];
#endif
        }
    }
}
