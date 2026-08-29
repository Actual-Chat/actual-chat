using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Build.Commands;

public enum AppPlatform
{
    Android = 0,
    Ios,
    Windows,
    MacOs,
}

/// <summary>
/// Options shared by <see cref="AppBuildCommand"/> and <see cref="AppRunCommand"/>.
/// Replaces the r-android*/r-windows*/run-ios*/run-macos* script family: the
/// platform is an argument, everything those scripts hardcoded is a flag.
/// </summary>
public class AppSettings : PlanSettings
{
    [CommandArgument(0, "<PLATFORM>")]
    [Description("android | ios | windows | macos")]
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
    [Description("Windows only: build an MSIX package instead of the unpackaged app")]
    public bool MustPackage { get; init; }

    [CommandOption("-l|--launch")]
    [Description("Launch the app after installing it (the default for 'app run')")]
    public bool MustLaunch { get; init; }

    [CommandOption("--no-launch")]
    [Description("Don't launch the app (the default for 'app build' and 'app install')")]
    public bool MustNotLaunch { get; init; }

    public string ResolvedConfiguration => IsRelease ? "Release" : Configuration;
    public bool IsDev => !IsProd;
    public bool IsWindowsPackaged
        // Native AOT publishes a self-contained exe rather than an MSIX, so it's never packaged
        => Platform == AppPlatform.Windows && MustPackage && !UseNativeAot;
    public bool MustUsePublish
        // Android is always published: that's where the signed APK comes from, in Debug too.
        => !MustNotPublish
            && (MustPublish
                || Platform == AppPlatform.Android
                || OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Release"));

    public bool ResolveMustLaunch(bool isLaunchedByDefault)
        => !MustNotLaunch && (MustLaunch || isLaunchedByDefault);

    public bool ResolveMustInstall(bool isInstalledByDefault, bool isLaunchedByDefault)
        // Only an unpackaged Windows build can be launched without deploying it first
        => isInstalledByDefault
            || (ResolveMustLaunch(isLaunchedByDefault)
                && (Platform != AppPlatform.Windows || IsWindowsPackaged));

    public override ValidationResult Validate()
    {
        if (MustPublish && MustNotPublish)
            return ValidationResult.Error("--publish and --no-publish are mutually exclusive.");
        if (MustLaunch && MustNotLaunch)
            return ValidationResult.Error("--launch and --no-launch are mutually exclusive.");
        if (MustLaunch && Platform is AppPlatform.Ios or AppPlatform.MacOs && OperatingSystem.IsWindows())
            return ValidationResult.Error($"Launching the {Platform} app needs macOS.");
        if (UseSimulator && Platform != AppPlatform.Ios)
            return ValidationResult.Error("--simulator is only supported for the ios platform.");
        if (MustPackage && Platform != AppPlatform.Windows)
            return ValidationResult.Error("--package is only supported for the windows platform.");
        if (UseNativeAot && Platform is AppPlatform.MacOs)
            return ValidationResult.Error("--aot is not wired for the macos platform.");
        if (!OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Debug")
            && !OrdinalIgnoreCaseEquals(ResolvedConfiguration, "Release"))
            return ValidationResult.Error($"Unknown configuration: {ResolvedConfiguration}.");

        return ValidationResult.Success();
    }

    // Private methods

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
