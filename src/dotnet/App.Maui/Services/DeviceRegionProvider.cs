namespace ActualChat.App.Maui.Services;

/// <summary>
/// Provides device region detection across different platforms.
/// This is necessary when InvariantGlobalization is enabled, as it disables .NET's RegionInfo APIs.
/// </summary>
public static class DeviceRegionProvider
{
    /// <summary>
    /// Gets the device region as a two-letter ISO 3166-1 alpha-2 country code (e.g., "US", "GB", "DE").
    /// </summary>
    /// <returns>Two-letter country code, or "US" as fallback.</returns>
    public static string GetDeviceRegion()
    {
        try {
#if ANDROID
            return GetAndroidRegion();
#elif IOS || MACCATALYST
            return GetIOSRegion();
#elif MACOS
            return GetMacOSRegion();
#elif WINDOWS
            return GetWindowsRegion();
#else
            return "US"; // Fallback for other platforms
#endif
        }
        catch {
            // Log the error if needed
            return "US";
        }
    }

#if ANDROID
    private static string GetAndroidRegion()
    {
        var context = Android.App.Application.Context;
        var telephonyManager = (Android.Telephony.TelephonyManager?)context
            .GetSystemService(Android.Content.Context.TelephonyService);

        if (telephonyManager != null) {
            // Try SIM card country first (most reliable)
            var simCountry = telephonyManager.SimCountryIso?.ToUpper();
            if (!string.IsNullOrEmpty(simCountry) && simCountry.Length == 2)
                return simCountry;

            // Fallback to network country
            var networkCountry = telephonyManager.NetworkCountryIso?.ToUpper();
            if (!string.IsNullOrEmpty(networkCountry) && networkCountry.Length == 2)
                return networkCountry;
        }

        // Last resort: device locale
        var locale = Java.Util.Locale.Default;
        var country = locale?.Country?.ToUpper();
        if (!string.IsNullOrEmpty(country) && country.Length == 2)
            return country;

        return "US"; // Final fallback
    }
#endif

#if IOS || MACCATALYST
    private static string GetIOSRegion()
    {
        // Try to get from carrier (SIM card) first
        var networkInfo = new CoreTelephony.CTTelephonyNetworkInfo();
        var carrier = networkInfo.ServiceSubscriberCellularProviders?.Values?.FirstOrDefault();
        var carrierCountry = carrier?.IsoCountryCode?.ToUpper();

        if (!string.IsNullOrEmpty(carrierCountry) && carrierCountry.Length == 2)
            return carrierCountry;

        // Fallback to device locale
        var locale = Foundation.NSLocale.CurrentLocale;
        var countryCode = locale.CountryCode?.ToUpper();

        if (!string.IsNullOrEmpty(countryCode) && countryCode.Length == 2)
            return countryCode;

        // Alternative: use RegionCode
        var regionCode = locale.RegionCode?.ToUpper();
        if (!string.IsNullOrEmpty(regionCode) && regionCode.Length == 2)
            return regionCode;

        return "US"; // Final fallback
    }
#endif

#if MACOS
    private static string GetMacOSRegion()
    {
        var locale = Foundation.NSLocale.CurrentLocale;
        var countryCode = (locale.CountryCode ?? locale.RegionCode)?.ToUpperInvariant();
        return countryCode is { Length: 2 } ? countryCode : "US";
    }
#endif

#if WINDOWS
    private static string GetWindowsRegion()
    {
        // Use Windows.System.UserProfile.GlobalizationPreferences
        // This works even with InvariantGlobalization enabled
        var geographicRegion = Windows.System.UserProfile.GlobalizationPreferences.HomeGeographicRegion;

        if (!string.IsNullOrEmpty(geographicRegion) && geographicRegion.Length == 2) {
            return geographicRegion.ToUpper();
        }

        // Fallback: try to get from the first language's region
        var languages = Windows.System.UserProfile.GlobalizationPreferences.Languages;
        if (languages.Count > 0) {
            var firstLanguage = languages[0];
            // Language format is like "en-US", extract the region part
            var parts = firstLanguage.Split('-');
            if (parts.Length >= 2 && parts[1].Length == 2)
                return parts[1].ToUpper();
        }

        return "US"; // Final fallback
    }
#endif
}
