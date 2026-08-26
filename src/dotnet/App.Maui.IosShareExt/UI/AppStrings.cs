using ActualChat.Localization;
using ActualChat.Maui;
using Microsoft.Extensions.Localization;

namespace ActualChat.App.Maui.IosShareExt.UI;

/// <summary>
/// The extension's <see cref="IStringLocalizer"/>, resolved against the language the app mirrors
/// into the App Group (<see cref="MauiPreferences.UILanguage"/>) - or, until the app has run once
/// since that key appeared, against the device's own language ordering.
/// </summary>
public static class AppStrings
{
    public static IStringLocalizer L => LanguageStringLocalizer.Get(UILanguage);
    private static Language UILanguage
        => MauiPreferences.UILanguage ?? Languages.DetectUILanguage(NSLocale.PreferredLanguages);
}
