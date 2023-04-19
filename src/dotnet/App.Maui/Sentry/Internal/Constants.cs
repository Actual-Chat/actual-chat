namespace Sentry.Maui.Internal;

internal static class Constants
{
    // See: https://github.com/getsentry/sentry-release-registry
    public const string SdkName = "sentry.dotnet.maui";

    // Since we incorporated Sentry.Maui, use version from Sentry assembly
    // public static string SdkVersion = typeof(SentryMauiOptions).Assembly
    public static string SdkVersion = typeof(SentryOptions).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
