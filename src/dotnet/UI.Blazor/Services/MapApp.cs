namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// A map app installed on the device. <see cref="Key"/> is opaque to the UI - it's whatever
/// the <see cref="ExternalMapOpener"/> that listed the app needs to launch it (a URL scheme on
/// iOS/Mac Catalyst, a package name on Android). <see cref="IconUrl"/> is a web or data URL.
/// </summary>
public record MapApp(string Key, string Title, string IconUrl, string UrlFormat = "");
