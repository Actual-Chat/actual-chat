using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Build.Commands;

/// <summary>
/// Builds the store package - the artifact uploaded to App Store / Play Store /
/// Microsoft Store - by running the same Bullseye publish target CI uses, then
/// reports where it landed.
/// </summary>
public sealed class AppPackCommand(CliContext context) : PlanCommand<AppPackCommand.Settings>(context)
{
    protected override CommandPlan GetPlan(Settings settings)
    {
        var (target, artifact) = GetTargetAndArtifact(settings);
        var isDevMaui = settings.IsDev;
        var useNativeAot = settings.UseNativeAot;
        var description = $"b {target} --configuration Release --is-dev-maui {isDevMaui.ToString().ToLower()}";
        if (settings.IsUniversal)
            description += " --universal";
        return new CommandPlan()
            .AddAction(description,
                ct => Program.RunTarget(target, "Release", isDevMaui, useNativeAot, ct, settings.IsUniversal))
            .AddOutput(artifact);
    }

    // Private methods

    private static (string Target, string Artifact) GetTargetAndArtifact(Settings settings)
    {
        const string publishDir = "artifacts/publish/App.Maui";
        var appId = settings.IsDev ? "chat.actual.dev.app" : "chat.actual.app";
        return settings.Platform switch {
            AppPlatform.Android => ("publish-android", $"{publishDir}/release_net11.0-android/{appId}-Signed.aab"),
            AppPlatform.Ios => ("publish-ios", $"{publishDir}/release_net11.0-ios_ios-arm64/ActualChat.ipa"),
            AppPlatform.Mac when settings.UseCatalyst => ("publish-maccatalyst",
                $"{publishDir}/release_net11.0-maccatalyst_maccatalyst-arm64/*.pkg"),
            AppPlatform.Mac => ("publish-mac", $"{publishDir}/release_net11.0-macos*/*.pkg"),
            AppPlatform.Windows => ("publish-win", "artifacts/AppPackages/**/App.Maui_*_x64.msix"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    // Nested types

    public sealed class Settings : PlanSettings
    {
        [CommandArgument(0, "<PLATFORM>")]
        [Description("android | ios | windows | mac (or macos)")]
        public AppPlatform Platform { get; init; }

        [CommandOption("--prod")]
        [Description("Pack the production app (IsDevMaui=false): voxt.ai instead of dev.voxt.ai")]
        public bool IsProd { get; init; }

        [CommandOption("--aot")]
        [Description("Build with Native AOT (currently wired for ios only)")]
        public bool UseNativeAot { get; init; }

        [CommandOption("--catalyst")]
        [Description("macos only: pack the Mac Catalyst app instead of the default AppKit one")]
        public bool UseCatalyst { get; init; }

        [CommandOption("--universal")]
        [Description("macos only (AppKit): a universal arm64 + x64 pkg, as CI builds it, instead of one for the host CPU")]
        public bool IsUniversal { get; init; }

        public bool IsDev => !IsProd;

        public override ValidationResult Validate()
        {
            if (UseNativeAot && Platform != AppPlatform.Ios)
                return ValidationResult.Error("--aot is only wired for the ios platform.");
            if (UseCatalyst && Platform != AppPlatform.Mac)
                return ValidationResult.Error("--catalyst is only supported for the macos platform.");
            if (IsUniversal && (Platform != AppPlatform.Mac || UseCatalyst))
                return ValidationResult.Error("--universal is only wired for the macos AppKit package.");
            if (Platform is AppPlatform.Ios or AppPlatform.Mac && !OperatingSystem.IsMacOS())
                return ValidationResult.Error($"Packing the {Platform} app needs macOS.");
            if (Platform is AppPlatform.Windows && !OperatingSystem.IsWindows())
                return ValidationResult.Error("Packing the Windows app needs Windows.");

            return ValidationResult.Success();
        }
    }
}
