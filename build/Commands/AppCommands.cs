using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Build.Commands;

[TypeConverter(typeof(AppPlatformConverter))]
public enum AppPlatform
{
    Android = 0,
    Ios,
    Windows,
    Mac,
}

// "macos" is accepted as a synonym of "mac" - both mean the AppKit app.
public sealed class AppPlatformConverter() : EnumConverter(typeof(AppPlatform))
{
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string s && s.Trim().Equals("macos", StringComparison.OrdinalIgnoreCase)
            ? AppPlatform.Mac
            : base.ConvertFrom(context, culture, value);
}

/// <summary>
/// Options shared by <see cref="AppBuildCommand"/> and <see cref="AppRunCommand"/>.
/// Replaces the r-android*/r-windows*/run-ios*/run-macos* script family: the
/// platform is an argument, everything those scripts hardcoded is a flag.
/// </summary>
public sealed class AppSettings : PlanSettings
{
    [CommandArgument(0, "<PLATFORM>")]
    [Description("android | ios | windows | mac (or macos)")]
    public AppPlatform Platform { get; init; }

    [CommandOption("-c|--configuration <CONFIGURATION>")]
    [Description("Debug or Release")]
    [DefaultValue("Debug")]
    public string Configuration { get; init; } = "Debug";

    [CommandOption("-r|--release")]
    [Description("Shorthand for --configuration Release")]
    public bool IsRelease { get; init; }

    [CommandOption("--prod")]
    [Description("Build the production app (IsDevMaui=false): voxt.ai instead of dev.voxt.ai")]
    public bool IsProd { get; init; }

    [CommandOption("--aot")]
    [Description("Build with Native AOT")]
    public bool UseNativeAot { get; init; }

    [CommandOption("--simulator")]
    [Description("iOS only: target a simulator instead of a connected device")]
    public bool UseSimulator { get; init; }

    [CommandOption("--catalyst")]
    [Description("macOS only: the Mac Catalyst app (net11.0-maccatalyst) instead of the default AppKit one (net11.0-macos)")]
    public bool UseCatalyst { get; init; }

    [CommandOption("--publish")]
    [Description("Force dotnet publish (the default for Release)")]
    public bool MustPublish { get; init; }

    [CommandOption("--no-publish")]
    [Description("Force dotnet build")]
    public bool MustNotPublish { get; init; }

    [CommandOption("--no-web")]
    [Description("Skip the npm web asset build")]
    public bool MustSkipWebBuild { get; init; }

    [CommandOption("--package")]
    [Description("Windows: build an MSIX package instead of the unpackaged app; implied elsewhere")]
    public bool MustPackage { get; init; }

    [CommandOption("--ci")]
    [Description("Android only: install the APK CI built on dev (newest release/* with --prod) instead of building")]
    public bool UseCiBuild { get; init; }

    [CommandOption("--ci-branch <BRANCH>")]
    [Description("Like --ci, but the APK CI built on this branch")]
    public string? CiBranch { get; init; }

    [CommandOption("-l|--launch")]
    [Description("Launch the app after installing it (the default for 'app run')")]
    public bool MustLaunch { get; init; }

    [CommandOption("--no-launch")]
    [Description("Don't launch the app (the default for 'app build' and 'app install')")]
    public bool MustNotLaunch { get; init; }

    public string ResolvedConfiguration => IsRelease ? "Release" : Configuration;
    public bool IsDev => !IsProd;
    public bool MustUseCiBuild => UseCiBuild || CiBranch is not null;
    public bool MustUsePublish
        // Android is always published: that's where the signed APK comes from, in Debug too.
        => !MustNotPublish
            && (MustPublish
                || Platform == AppPlatform.Android
                || OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Release"));

    // Installing on Windows means registering an MSIX, so "app install" implies --package
    public bool ResolveIsWindowsPackaged(bool mustInstall)
        => Platform == AppPlatform.Windows && (MustPackage || mustInstall) && !UseNativeAot;

    public bool ResolveMustLaunch(bool isLaunchedByDefault)
        => !MustNotLaunch && (MustLaunch || isLaunchedByDefault);

    public bool ResolveMustInstall(bool isInstalledByDefault, bool isLaunchedByDefault)
        // Only an unpackaged Windows build can be launched without deploying it first
        => isInstalledByDefault
            || (ResolveMustLaunch(isLaunchedByDefault)
                && (Platform != AppPlatform.Windows || (MustPackage && !UseNativeAot)));

    public override ValidationResult Validate()
    {
        if (MustPublish && MustNotPublish)
            return ValidationResult.Error("--publish and --no-publish are mutually exclusive.");
        if (MustLaunch && MustNotLaunch)
            return ValidationResult.Error("--launch and --no-launch are mutually exclusive.");
        if (Platform is AppPlatform.Ios or AppPlatform.Mac && OperatingSystem.IsWindows())
            return ValidationResult.Error($"Building the {Platform} app needs macOS.");
        if (UseSimulator && Platform != AppPlatform.Ios)
            return ValidationResult.Error("--simulator is only supported for the ios platform.");
        if (UseCatalyst && Platform != AppPlatform.Mac)
            return ValidationResult.Error("--catalyst is only supported for the macos platform.");
        if (MustUseCiBuild && Platform != AppPlatform.Android)
            return ValidationResult.Error("--ci is only supported for the android platform.");
        if (MustUseCiBuild && (UseNativeAot || MustPublish || MustNotPublish
                || !OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Debug")))
            return ValidationResult.Error("--ci installs the APK CI built, so build options don't apply to it.");
        if (!OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Debug")
            && !OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Release"))
            return ValidationResult.Error($"Unknown configuration: {ResolvedConfiguration}.");

        return Platform is AppPlatform.Ios or AppPlatform.Mac
            ? ValidateApple()
            : ValidationResult.Success();
    }

    // Private methods

    private ValidationResult ValidateApple()
    {
        // Release is signed for store distribution and Native AOT applies to Release only, so neither
        // can be installed from here; a Debug ios build has the dev provisioning profile only.
        var packCommand = $"'b app pack {Platform.ToString().ToLowerInvariant()}'";
        if (!OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Debug"))
            return ValidationResult.Error(
                $"A Release {Platform} build is the store package, signed for distribution - use {packCommand}.");
        if (UseNativeAot)
            return ValidationResult.Error(
                $"Native AOT applies to the {Platform} store package only - use {packCommand}.");
        if (MustPublish)
            return ValidationResult.Error($"--publish is not supported for the {Platform} platform.");
        if (IsProd && Platform is AppPlatform.Ios)
            return ValidationResult.Error(
                "A Debug Ios build is signed with the dev provisioning profile, so there is no --prod one.");

        return ValidationResult.Success();
    }

    private static bool OrdinalIgnoreCaseEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class AppBuildCommand(CliContext context) : PlanCommand<AppSettings>(context)
{
    protected override CommandPlan GetPlan(AppSettings settings)
        => MauiApp.GetPlan(settings, false, false);
}

public sealed class AppInstallCommand(CliContext context) : PlanCommand<AppSettings>(context)
{
    protected override CommandPlan GetPlan(AppSettings settings)
        => MauiApp.GetPlan(settings, true, false);
}

public sealed class AppRunCommand(CliContext context) : PlanCommand<AppSettings>(context)
{
    protected override CommandPlan GetPlan(AppSettings settings)
        => MauiApp.GetPlan(settings, false, true);
}
