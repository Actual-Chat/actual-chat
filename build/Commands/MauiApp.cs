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

    public static CommandPlan GetPlan(AppSettings settings, bool isInstalledByDefault, bool isLaunchedByDefault)
    {
        var mustLaunch = settings.ResolveMustLaunch(isLaunchedByDefault);
        var mustInstall = settings.ResolveMustInstall(isInstalledByDefault, isLaunchedByDefault);
        var plan = new CommandPlan();
        // The Apple scripts build the web assets and the app themselves, as one unit.
        if (settings.Platform is AppPlatform.Ios or AppPlatform.MacOs) {
            if (mustInstall && !mustLaunch)
                throw new WithoutStackException(
                    $"{settings.Platform} can't install without launching - use 'b app run' or 'b app build'.");

            if (mustLaunch)
                AddScript(plan, settings);

            return plan;
        }

        if (!settings.MustSkipWebBuild)
            plan.AddRun(Utils.FindNpmExe(), ["run", $"build:{settings.ResolvedConfiguration}"]);

        AddDotnet(plan, settings);
        // Announced before the install step, so the path stays visible once the app takes over the console.
        if (GetArtifactPath(settings) is { } artifactPath)
            plan.AddOutput(artifactPath);
        if (mustInstall)
            AddInstall(plan, settings);
        if (mustLaunch)
            AddLaunch(plan, settings);

        return plan;
    }

    // Private methods

    private static void AddDotnet(CommandPlan plan, AppSettings settings)
    {
        var args = new List<string> {
            settings.MustUsePublish ? "publish" : "build",
            ProjectDir,
            "-noLogo",
            "-c", settings.ResolvedConfiguration,
            "-f", GetTargetFramework(settings.Platform),
        };
        if (!settings.IsDev)
            args.Add("-p:IsDevMaui=false");
        if (settings.UseNativeAot)
            args.Add("-p:UseNativeAot=true");
        switch (settings.Platform) {
        case AppPlatform.Android:
            if (settings.IsDev)
                args.Add("-p:EmbedAssembliesIntoApk=true");
            else
                AddAndroidSigning(plan, args);
            break;
        case AppPlatform.Windows:
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

    private static void AddInstall(CommandPlan plan, AppSettings settings)
    {
        // Windows runs unpackaged - the build output is already "installed".
        if (settings.Platform != AppPlatform.Android || GetArtifactPath(settings) is not { } apkPath)
            return;

        plan.Add(new RunStep("adb", ["install", "-r", apkPath]) { RequiredPath = apkPath });
    }

    private static void AddLaunch(CommandPlan plan, AppSettings settings)
    {
        switch (settings.Platform) {
        case AppPlatform.Android:
            // monkey launches by package, so the generated MainActivity name isn't needed.
            plan.AddRun("adb",
                ["shell", "monkey", "-p", GetAppId(settings), "-c", "android.intent.category.LAUNCHER", "1"]);
            break;
        case AppPlatform.Windows when GetArtifactPath(settings) is { } exePath:
            plan.Add(new RunStep(exePath, []) { RequiredPath = exePath });
            break;
        }
    }

    private static void AddScript(CommandPlan plan, AppSettings settings)
    {
        var scriptName = settings switch {
            { Platform: AppPlatform.MacOs } => "run-macos.sh",
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

    private static string GetAppId(AppSettings settings)
        => settings.IsDev ? "chat.actual.dev.app" : "chat.actual.app";

    private static string GetTargetFramework(AppPlatform platform)
        => platform switch {
            AppPlatform.Android => $"{TargetFrameworkPrefix}-android",
            AppPlatform.Ios => $"{TargetFrameworkPrefix}-ios",
            AppPlatform.Windows => WindowsTargetFramework,
            AppPlatform.MacOs => $"{TargetFrameworkPrefix}-maccatalyst",
            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
        };

    private static string GetOutputDir(AppSettings settings)
    {
        // The artifacts layout pivot is "<lowercase configuration>_<target framework>[_<rid>]".
        var kind = settings.MustUsePublish ? "publish" : "bin";
        var pivot = settings.ResolvedConfiguration.ToLower() + "_" + GetTargetFramework(settings.Platform);
        if (settings is { Platform: AppPlatform.Windows, MustUsePublish: true })
            pivot += "_win-x64";

        return Path.Combine("artifacts", kind, "App.Maui", pivot);
    }
}
