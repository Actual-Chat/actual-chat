using System.Globalization;

namespace ActualChat.Localization;

/// <summary>
/// The ambient UI language used to resolve .resx resources.
/// Deliberately separate from <see cref="CultureInfo.CurrentUICulture"/>,
/// which stays invariant everywhere in this app.
/// </summary>
public static class UILanguage
{
    private static readonly AsyncLocal<CultureInfo?> CurrentLocal = new();

    public static CultureInfo Current => CurrentLocal.Value ?? CultureInfo.InvariantCulture;

    public static ClosedDisposable<CultureInfo?> Change(CultureInfo? culture)
    {
        var oldCulture = CurrentLocal.Value;
        CurrentLocal.Value = culture;
        return new ClosedDisposable<CultureInfo?>(oldCulture, static x => CurrentLocal.Value = x);
    }

    public static ClosedDisposable<CultureInfo?> Change(string? isoCode)
        => Change(ToCulture(isoCode));

    public static CultureInfo? ToCulture(string? isoCode)
    {
        // Keyed by the primary subtag ("ru", not "ru-RU"): one .resx per language, not per region.
        if (isoCode.IsNullOrEmpty())
            return null;

        var separatorIndex = isoCode.IndexOf('-');
        var primary = separatorIndex < 0 ? isoCode : isoCode[..separatorIndex];
        if (string.Equals(primary, "en", StringComparison.OrdinalIgnoreCase))
            return CultureInfo.InvariantCulture;

        try {
            return CultureInfo.GetCultureInfo(primary);
        }
        catch (CultureNotFoundException) {
            return null;
        }
    }
}
