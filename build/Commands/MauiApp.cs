using Spectre.Console;

namespace Build.Commands;

/// <summary>
/// Describes how the MAUI app is built, installed and launched. Absorbs the
/// r-android*, r-windows*, run-android, run-ios*, run-macos script family; the
/// platform-specific Apple tooling still lives in the scripts under <c>scripts/</c>.
/// </summary>
internal static class MauiApp
{
    private const string TargetFrameworkPrefix = "net11.0";
    private const string WindowsTargetFramework = $"{TargetFrameworkPrefix}-windows10.0.22621.0";
    private const string ProjectDir = "src/dotnet/App.Maui";
    // Windows PowerShell rather than pwsh: the Appx module is in-box there, and pwsh may not be installed
    private const string PowerShellExe = "powershell";

    public static CommandPlan GetPlan(AppSettings settings, bool isInstalledByDefault, bool isLaunchedByDefault)
    {
        if (settings.MustPackage && settings.Platform != AppPlatform.Windows)
            AnsiConsole.MarkupLine(
                $"[yellow]--package is redundant:[/] {settings.Platform} builds are always packaged.");

        var mustLaunch = settings.ResolveMustLaunch(isLaunchedByDefault);
        var mustInstall = settings.ResolveMustInstall(isInstalledByDefault, isLaunchedByDefault);
        var plan = new CommandPlan();
        // The Apple scripts build the web assets and the app themselves, as one unit.
        if (settings.Platform is AppPlatform.Ios or AppPlatform.Mac) {
            if (mustInstall && !mustLaunch)
                throw new WithoutStackException(
                    $"{settings.Platform} can't install without launching - use 'b app run' or 'b app build'.");

            if (mustLaunch)
                AddScript(plan, settings);

            return plan;
        }

        var isPackaged = settings.ResolveIsWindowsPackaged(mustInstall);
        // Native AOT publishes a self-contained exe, so there's no package to register
        if (mustInstall && settings.Platform == AppPlatform.Windows && !isPackaged)
            throw new WithoutStackException(
                "A Native AOT Windows build has nothing to install - it runs from artifacts/ as-is."
                + " Drop --aot, or use 'b app build windows --aot'.");

        if (!settings.MustSkipWebBuild)
            plan.AddRun(Utils.FindNpmExe(), ["run", $"build:{settings.ResolvedConfiguration}"]);

        AddDotnet(plan, settings, isPackaged);
        // Announced before the install step, so the path stays visible once the app takes over the console.
        if (GetArtifactPath(settings) is { } artifactPath)
            plan.AddOutput(artifactPath);
        if (mustInstall)
            AddInstall(plan, settings, isPackaged);
        if (mustLaunch)
            AddLaunch(plan, settings, isPackaged);

        return plan;
    }

    // Private methods

    private static void AddDotnet(CommandPlan plan, AppSettings settings, bool isPackaged)
    {
        var args = new List<string> {
            settings.MustUsePublish ? "publish" : "build",
            ProjectDir,
            "-noLogo",
            "-c", settings.ResolvedConfiguration,
            "-f", GetTargetFramework(settings),
        };
        if (!settings.IsDev)
            args.Add("-p:IsDevMaui=false");
        if (settings.UseNativeAot)
            args.Add("-p:UseNativeAot=true");
        switch (settings.Platform) {
        case AppPlatform.Android:
            if (!settings.IsDev)
                AddAndroidSigning(plan, args);
            break;
        case AppPlatform.Windows:
            if (!isPackaged)
                args.Add("-p:WindowsPackageType=None");
            break;
        }

        args.AddRange(settings.ExtraArgs);
        plan.AddRun(Utils.FindDotnetExe(), args);
    }

    private static void AddAndroidSigning(CommandPlan plan, List<string> args)
    {
        var keyPass = Environment.GetEnvironmentVariable("ActualChat_AndroidSigningKeyPass");
        var storePass = Environment.GetEnvironmentVariable("ActualChat_AndroidSigningStorePass");
        if (keyPass is null || storePass is null)
            throw new WithoutStackException(
                "--prod Android builds need ActualChat_AndroidSigningKeyPass and ActualChat_AndroidSigningStorePass.");

        plan.AddSecret(keyPass).AddSecret(storePass);
        args.Add($"-p:AndroidSigningKeyPass={keyPass}");
        args.Add($"-p:AndroidSigningStorePass={storePass}");
    }

    private static void AddInstall(CommandPlan plan, AppSettings settings, bool isPackaged)
    {
        switch (settings.Platform) {
        case AppPlatform.Android when GetArtifactPath(settings) is { } apkPath:
            plan.Add(new RunStep("adb", ["install", "-r", apkPath]) { RequiredPath = apkPath });
            break;
        case AppPlatform.Windows when isPackaged:
            var manifestPath = GetAppxManifestPath(settings);
            plan.Add(PowerShell($"Add-AppxPackage -Register '{manifestPath}'"
                    + " -ForceUpdateFromAnyVersion -ForceApplicationShutdown")
                with { RequiredPath = manifestPath });
            break;
        }
    }

    private static void AddLaunch(CommandPlan plan, AppSettings settings, bool isPackaged)
    {
        switch (settings.Platform) {
        case AppPlatform.Android:
            // monkey launches by package, so the generated MainActivity name isn't needed.
            plan.AddRun("adb",
                ["shell", "monkey", "-p", GetAppId(settings), "-c", "android.intent.category.LAUNCHER", "1"]);
            break;
        case AppPlatform.Windows when isPackaged:
            // A packaged app can't be started by its .exe - it has to be activated by package family
            // name, and the name comes from the manifest the build just generated.
            plan.Add(PowerShell(
                $"$n = ([xml](Get-Content '{GetAppxManifestPath(settings)}')).Package.Identity.Name;"
                + " $p = (Get-AppxPackage -Name $n).PackageFamilyName;"
                + " if (-not $p) { throw ('Not registered: ' + $n) };"
                + @" Start-Process ('shell:AppsFolder\' + $p + '!App')"));
            break;
        case AppPlatform.Windows when GetArtifactPath(settings) is { } exePath:
            plan.Add(new RunStep(exePath, []) { RequiredPath = exePath });
            break;
        }
    }

    private static void AddScript(CommandPlan plan, AppSettings settings)
    {
        var scriptName = settings switch {
            { Platform: AppPlatform.Mac, UseCatalyst: true } => "run-maccatalyst.sh",
            { Platform: AppPlatform.Mac } => "run-mac.sh",
            { UseSimulator: true } => "run-ios-simulator.sh",
            _ => "run-ios.sh",
        };
        var scriptPath = Path.Combine("scripts", scriptName);
        plan.Add(new RunStep("bash", [scriptPath]) { RequiredPath = scriptPath });
    }

    private static string? GetArtifactPath(AppSettings settings)
        => settings.Platform switch {
            AppPlatform.Android => Path.Combine(GetOutputDir(settings), $"{GetAppId(settings)}-Signed.apk"),
            AppPlatform.Windows => Path.Combine(GetOutputDir(settings), "ActualChat.exe"),
            _ => null,
        };

    private static RunStep PowerShell(string command)
        // Arguments reach the process unescaped (see CommandPlan.RunStepAsync), so -Command is
        // quoted here rather than at every call site - keep double quotes out of the script itself
        => new(PowerShellExe, ["-NoProfile", "-NonInteractive", "-Command", $"\"{command}\""]);

    private static string GetAppxManifestPath(AppSettings settings)
        // AppX/ is the MSIX layout MSBuild stages, and the one VS and Rider deploy; the manifest in
        // the output root looks the same but registering it breaks the WebView's own content.
        // Absolute, because Add-AppxPackage rejects a relative path.
        => Path.GetFullPath(Path.Combine(GetOutputDir(settings), "AppX", "AppxManifest.xml"));

    private static string GetAppId(AppSettings settings)
        => settings.IsDev ? "chat.actual.dev.app" : "chat.actual.app";

    private static string GetTargetFramework(AppSettings settings)
        => settings switch {
            { Platform: AppPlatform.Android } => $"{TargetFrameworkPrefix}-android",
            { Platform: AppPlatform.Ios } => $"{TargetFrameworkPrefix}-ios",
            { Platform: AppPlatform.Windows } => WindowsTargetFramework,
            { Platform: AppPlatform.Mac, UseCatalyst: true } => $"{TargetFrameworkPrefix}-maccatalyst",
            { Platform: AppPlatform.Mac } => $"{TargetFrameworkPrefix}-macos",
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        };

    private static string GetOutputDir(AppSettings settings)
    {
        // The artifacts layout pivot is "<lowercase configuration>_<target framework>[_<rid>]".
        var kind = settings.MustUsePublish ? "publish" : "bin";
        var pivot = settings.ResolvedConfiguration.ToLower() + "_" + GetTargetFramework(settings);
        if (settings is { Platform: AppPlatform.Windows, MustUsePublish: true })
            pivot += "_win-x64";

        return Path.Combine("artifacts", kind, "App.Maui", pivot);
    }
}
