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
        return new CommandPlan()
            .AddAction(description,
                ct => Program.RunTarget(target, "Release", isDevMaui, useNativeAot, ct))
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
            AppPlatform.MacOs => ("publish-maccatalyst",
                $"{publishDir}/release_net11.0-maccatalyst_maccatalyst-arm64/*.pkg"),
            AppPlatform.Windows => ("publish-win", "src/dotnet/App.Maui/AppPackages/**/App.Maui_*_x64.msix"),
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };
    }

    // Nested types

    public sealed class Settings : PlanSettings
    {
        [CommandArgument(0, "<PLATFORM>")]
        [Description("android | ios | windows | macos")]
        public AppPlatform Platform { get; init; }

        [CommandOption("--prod")]
        [Description("Pack the production app (IsDevMaui=false): voxt.ai instead of dev.voxt.ai")]
        public bool IsProd { get; init; }

        [CommandOption("--aot")]
        [Description("Build with Native AOT (currently wired for ios only)")]
        public bool UseNativeAot { get; init; }

        public bool IsDev => !IsProd;

        public override ValidationResult Validate()
        {
            if (UseNativeAot && Platform != AppPlatform.Ios)
                return ValidationResult.Error("--aot is only wired for the ios platform.");
            if (Platform is AppPlatform.Ios or AppPlatform.MacOs && !OperatingSystem.IsMacOS())
                return ValidationResult.Error($"Packing the {Platform} app needs macOS.");
            if (Platform is AppPlatform.Windows && !OperatingSystem.IsWindows())
                return ValidationResult.Error("Packing the Windows app needs Windows.");

            return ValidationResult.Success();
        }
    }
}
