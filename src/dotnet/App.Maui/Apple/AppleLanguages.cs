using Foundation;

namespace ActualChat.App.Maui;

/// <summary>
/// Makes the app's own UI language the one the OS renders it in: the explicit selection goes into
/// the app's <c>AppleLanguages</c> default - what iOS reads to pick the lproj for permission
/// prompts and the share sheet - and auto removes it, so the device language applies again.
/// Foundation reads the default at process start, so a change shows from the next launch on.
/// </summary>
public static class AppleLanguages
{
    private const string Key = "AppleLanguages";
    public static void Set(Language? selected)
    {
        // Removed rather than left alone on auto: the WebView's language list follows this key,
        // so a stale value would keep "auto" resolving to it instead of the device language.
        var defaults = NSUserDefaults.StandardUserDefaults;
        if (selected == null)
            defaults.RemoveObject(Key);
        else
            defaults[Key] = NSArray.FromStrings(selected.IsoCode);
    }
}
