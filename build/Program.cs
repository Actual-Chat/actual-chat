using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bullseye;
using CliWrap;
using CliWrap.Buffered;
using Crayon;
using static Bullseye.Targets;
using static Crayon.Output;
using static Build.CliWrapCommandExtensions;

namespace Build;


// DragonFruit approach doesn't the best option here (for example currently it doesn't support aliases),
// but it's clean and works, so I've decided to use it.
internal static class Program
{
    public static class Targets
    {
        public const string CleanDist = "clean-dist";
        public const string Clean = "clean";
        public const string Watch = "watch";
        public const string NpmInstall = "npm-install";
        public const string UnitTests = "unit-tests";
        public const string GenerateVersion = "generate-version";
        public const string IntegrationTests = "integration-tests";
        public const string CleanTests = "clean-tests";
        public const string Tests = "tests";
        public const string Build = "build";
        public const string Maui = "maui";
        public const string PublishWin = "publish-win";
        public const string RestoreTools = "restore-tools";
        public const string Restore = "restore";
        public const string Default = "default";
    }

    /// <summary>Build project for repository</summary>
    /// <param name="arguments">A list of targets to run or list.</param>
    /// <param name="clear">Clear the console before execution.</param>
    /// <param name="dryRun">Do a dry run without executing actions.</param>
    /// <param name="host">Force the mode for a specific host environment (normally auto-detected).</param>
    /// <param name="listDependencies">List all (or specified) targets and dependencies, then exit.</param>
    /// <param name="listInputs">List all (or specified) targets and inputs, then exit.</param>
    /// <param name="listTargets">List all (or specified) targets, then exit.</param>
    /// <param name="listTree">List all (or specified) targets and dependency trees, then exit.</param>
    /// <param name="noColor">Disable colored output.</param>
    /// <param name="noExtendedChars">Gets or sets a value indicating whether to disable extended characters.</param>
    /// <param name="parallel">Run targets in parallel.</param>
    /// <param name="skipDependencies">Do not run targets' dependencies.</param>
    /// <param name="verbose">Enable verbose output.</param>
    /// <param name="cancellationToken">The terminate program cancellation</param>
    /// <param name="configuration">The configuration for building</param>
    private static async Task<int> Main(
        string[] arguments,
        bool clear,
        bool dryRun,
        Host host,
        bool listDependencies,
        bool listInputs,
        bool listTargets,
        bool listTree,
        bool noColor,
        bool noExtendedChars,
        bool parallel,
        bool skipDependencies,
        bool verbose,
        CancellationToken cancellationToken,
        // our options here
        string configuration = "Debug"
        )
    {
        Console.OutputEncoding = Encoding.UTF8;
        SetEnvVariables();
        PrintHeader();

        var options = new Options {
            Clear = clear,
            DryRun = dryRun,
            Host = host,
            ListDependencies = listDependencies,
            ListInputs = listInputs,
            ListTargets = listTargets,
            ListTree = listTree,
            NoColor = noColor,
            Parallel = parallel,
            SkipDependencies = skipDependencies,
            Verbose = verbose,
            NoExtendedChars = noExtendedChars,
        };

        var dotnet = Utils.FindDotnetExe();

        Target(Targets.Watch, DependsOn(Targets.CleanDist), async () => {

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                throw new WithoutStackException($"Watch is not implemented for '{RuntimeInformation.OSDescription}'. Use dotnet watch + webpack watch without build system");

            // if one of process exits then close another on Cancel()
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // cliwrap doesn't kill children of the process..
            // https://github.com/Tyrrrz/CliWrap/blob/49f34ad145501da2d5381058a9ab8d336e788511/CliWrap/Utils/Polyfills.cs#L117
            using var dotnetWatch = ProcessWatch.Start(
                "dotnet",
                dotnet,
                "watch -v run",
                "src/dotnet/App.Server",
                new () {

                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["EnableAnalyzer"] = "false",
                    ["EnableNETAnalyzers"] = "false"
                },
                cts);
            using var npmWatch = ProcessWatch.Start(
                "webpack",
                Utils.FindNpmExe(),
                "run watch",
                "src/nodejs",
                new () {
                    ["CI"] = "true"
                },
                cts);
            await Task.WhenAny(npmWatch.WaitForExit(), dotnetWatch.WaitForExit()).ConfigureAwait(false);

            Console.WriteLine(Yellow("The watching is over"));
        });

        Target(Targets.CleanDist, () => {
            FileExt.Remove("src/dotnet/*/wwwroot/dist");
        });

        Target(Targets.Clean, DependsOn(Targets.CleanDist, Targets.CleanTests), () => {
            FileExt.Remove("artifacts", "src/nodejs/node_modules", "**/obj/");
        });

        Target(Targets.NpmInstall, async () => {
            var nodeModulesDir = Path.Combine("src", "nodejs", "node_modules");
            if (!Directory.Exists(nodeModulesDir)) {
                await Npm()
                    .WithArguments("ci")
                    .ToConsole(Blue("npm install: "))
                    .ExecuteAsync(cancellationToken)
                    .Task.ConfigureAwait(false);
            }
        });

        Target(Targets.UnitTests,
            async () => {
                await Cli.Wrap(dotnet)
                    .WithArguments("test",
                        "ActualChat.sln",
                        "--nologo",
                        "--filter \"FullyQualifiedName~UnitTests\"",
                        "--no-restore",
                        "--blame-hang",
                        "--blame-hang-timeout 60s",
                        "--logger \"console;verbosity=detailed\"",
                        "--logger \"trx;LogFileName=Results.trx\"",
                        Utils.GithubLogger(),
                        $"-c {configuration} "
                    )
                    .ToConsole()
                    .ExecuteBufferedAsync(cancellationToken)
                    .Task.ConfigureAwait(false);
            });

        Target(Targets.GenerateVersion, async () => {
            var cmd = await Cli.Wrap(dotnet)
                .WithArguments("nbgv get-version --format json")
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);
            #pragma warning disable IL2026
            var nbgv = JsonSerializer.Deserialize<NbgvModel>(cmd.StandardOutput ?? throw new WithoutStackException("nbgv returned null"));

            if (nbgv == null)
                throw new WithoutStackException("nbgv returned empty");

            var dict = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "BuildVersion", nbgv.Version ?? "0.0.0.0" },
                { "AssemblyInformationalVersion", nbgv.AssemblyInformationalVersion ?? "0.0.0.0" },
                { "AssemblyFileVersion", nbgv.AssemblyFileVersion ?? "0.0.0.0" },
                { "FileVersion", nbgv.AssemblyFileVersion ?? "0.0.0.0" },
                { "BuildVersionSimple", nbgv.SimpleVersion ?? "0.0.0.0" },
                { "NuGetPackageVersion", nbgv.NuGetPackageVersion ?? "0.0.0.0" },
                { "Version", nbgv.NuGetPackageVersion ?? nbgv.SemVer2 ?? "0.0.0.0" },
                { "PackageVersion", nbgv.NuGetPackageVersion ?? nbgv.SemVer2 ?? "0.0.0.0" },
                { "NPMPackageVersion", nbgv.NuGetPackageVersion ?? nbgv.SemVer2 ?? "0.0.0.0" },
                { "MajorMinorVersion", nbgv.MajorMinorVersion ?? "0.0" },
                { "AssemblyVersion", nbgv.AssemblyVersion ?? "0.0.0.0" },
            };
            var dictWithCondition = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "PrereleaseVersion", nbgv.PrereleaseVersion ?? "" },
                { "PrereleaseVersionNoLeadingHyphen", nbgv.PrereleaseVersionNoLeadingHyphen ?? "" },
                { "GitCommitId", nbgv.GitCommitId ?? "4b825dc642cb6eb9a060e54bf8d69288fbee4904" },
                { "GitCommitIdShort", nbgv.GitCommitIdShort ?? "4b825dc642" },
                { "GitCommitDateTicks", nbgv.GitCommitDate.Ticks.ToString(CultureInfo.InvariantCulture)},
                { "GitVersionHeight", nbgv.VersionHeight.ToString(CultureInfo.InvariantCulture) },
                { "BuildNumber", nbgv.BuildNumber.ToString(CultureInfo.InvariantCulture) },
                { "BuildVersionNumberComponent", nbgv.BuildNumber.ToString(CultureInfo.InvariantCulture)},
                { "PublicRelease", nbgv.PublicRelease.ToString() },
                { "CloudBuildNumber", nbgv.CloudBuildNumber ?? nbgv.NuGetPackageVersion ?? "0.0.0.0" },
                { "SemVerBuildSuffix", nbgv.BuildMetadataFragment ?? "" },
                { "ChocolateyPackageVersion", nbgv.ChocolateyPackageVersion ?? nbgv.NuGetPackageVersion ?? "0.0.0.0" },
            };

            var sb = new StringBuilder(1024);
            sb.AppendLine("  <PropertyGroup>");
            foreach (var (key, val) in dict) {
                sb.AppendLine($"    <{key}>{val}</{key}>");
            }
            foreach (var (key, val) in dictWithCondition) {
                sb.AppendLine($"    <{key} Condition=\"'$({key})' == ''\">{val}</{key}>");
            }
            sb.AppendLine("  </PropertyGroup>");
            var props = sb.ToString();
            var tasks = new[] {
                File.WriteAllTextAsync("Nerdbank.GitVersioning.targets", $"<Project>\n<Target Name=\"GetBuildVersion\">\n{props}</Target>\n</Project>"),
                // we need GitCommitId and other before *.targets is loaded
                File.WriteAllTextAsync("Nerdbank.GitVersioning.props", $"<Project>\n{props}</Project>"),
            };
            await Task.WhenAll(tasks).ConfigureAwait(false);
            Console.WriteLine($"Generated version is {Bold(Yellow(nbgv.NuGetPackageVersion ?? nbgv.SemVer2 ?? "0.0.0.0"))}");

            if (!File.Exists(".dockerignore"))
                return;

            var dockerIgnore = await File.ReadAllTextAsync(".dockerignore").ConfigureAwait(false);
            if (!dockerIgnore.Contains(".git")) {
                dockerIgnore = ".git\n" + dockerIgnore;
            }
            else {
                // uncomment any .git pattern
                const RegexOptions regexOptions = RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase;
                dockerIgnore = Regex.Replace(dockerIgnore, "^[^#]*[#]+(.*\\.git.*)$", "$1", regexOptions, TimeSpan.FromSeconds(5));
            }
            await File.WriteAllTextAsync(".dockerignore", dockerIgnore).ConfigureAwait(false);
        });

        Target(Targets.IntegrationTests, async () => {
            await Cli.Wrap(dotnet)
                .WithArguments("test",
                    "ActualChat.sln",
                    "--nologo",
                    "--filter \"FullyQualifiedName~IntegrationTests&FullyQualifiedName!~UI.Blazor.IntegrationTests\"",
                    "--no-restore",
                    "--blame-hang",
                    "--blame-hang-timeout 300s",
                    "--logger \"console;verbosity=detailed\"",
                    "--logger \"trx;LogFileName=Results.trx\"",
                    Utils.GithubLogger(),
                    $"-c {configuration}")
                .ToConsole()
                .ExecuteBufferedAsync(cancellationToken)
                .Task.ConfigureAwait(false);
        });

        Target(Targets.CleanTests, () => {
            FileExt.Remove("artifacts/tests/output");
        });

        Target(Targets.Tests, DependsOn(Targets.CleanTests, Targets.UnitTests, Targets.IntegrationTests), () => { });

        Target(Targets.Build, DependsOn(Targets.CleanDist, Targets.NpmInstall), async () => {
            // if one of process exits then close another on Cancel()
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cts.Token;
            try {
                var dotnetTask = Cli
                    .Wrap(dotnet)
                    .WithArguments($"build ActualChat.sln -noLogo -maxCpuCount -nodeReuse:false -c {configuration}")
                    .ToConsole(Green("dotnet: "))
                    .ExecuteAsync(token).Task;

                var npmTask = Npm()
                    .WithArguments($"run build:{configuration}")
                    .ToConsole(Blue("webpack: "))
                    .ExecuteAsync(token).Task;

                await Task.WhenAll(dotnetTask, npmTask).ConfigureAwait(false);
            }
            finally {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
                cts.Dispose();
            }
        });

        Target(Targets.Maui, DependsOn(Targets.CleanDist, Targets.NpmInstall), async () => {
            // if one of process exits then close another on Cancel()
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cts.Token;
            try {
                var dotnetTask = Cli
                    .Wrap(dotnet)
                    .WithArguments($"build -noLogo -maxCpuCount -nodeReuse:false -c {configuration}")
                    .WithWorkingDirectory(Path.Combine("src", "dotnet", "App.Maui"))
                    .ToConsole(Green("dotnet: "))
                    .ExecuteAsync(token).Task;

                var npmTask = Npm()
                    .WithArguments($"run build:{configuration}")
                    .ToConsole(Blue("webpack: "))
                    .ExecuteAsync(token).Task;

                await Task.WhenAll(dotnetTask, npmTask).ConfigureAwait(false);
            }
            finally {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
                cts.Dispose();
            }
        });

        Target(Targets.PublishWin, DependsOn(Targets.CleanDist, Targets.NpmInstall), async () => {
            await Npm()
                .WithArguments($"run build:{configuration}")
                .ToConsole(Blue("webpack: "))
                .ExecuteAsync(cancellationToken).Task;

            var isProduction = configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);
            await AppxManifestGenerator.Generate(
                isProduction,
                cancellationToken);
            await Cli
                .Wrap(dotnet)
                .WithArguments("publish",
                    "-noLogo",
                    "-maxCpuCount",
                    "-nodeReuse:false",
                    "-f net7.0-windows10.0.22000.0",
                    "-p:RuntimeIdentifierOverride=win10-x64",
                    $"-c {configuration}",
                    isProduction ? "-p:IsDevMaui=false" : "")
                .WithWorkingDirectory("src/dotnet/App.Maui")
                .ToConsole(Green("dotnet: "))
                .ExecuteAsync(cancellationToken)
                .Task;
        });

        Target(Targets.RestoreTools, async () => {
            await Cli.Wrap(dotnet).WithArguments("tool restore")
                .ToConsole()
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync().Task.ConfigureAwait(false);

            await Cli.Wrap(dotnet).WithArguments("playwright install")
                .ToConsole()
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync().Task.ConfigureAwait(false);

            await Cli.Wrap(dotnet).WithArguments("workload install wasm-tools")
                .ToConsole()
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync().Task.ConfigureAwait(false);
        });

        Target(Targets.Restore, async () => {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try {
                await Cli.Wrap(dotnet).WithArguments("restore ActualChat.sln")
                    .ToConsole(Green("dotnet restore: "))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cts.Token).Task.ConfigureAwait(false);
            }
            finally {
                cts.Cancel();
                cts.Dispose();
            }
        });

        Target(Targets.Default, DependsOn(Targets.Build));

        try {
            /// <see cref="RunTargetsAndExitAsync"/> will hang Target on ctrl+c
            await RunTargetsWithoutExitingAsync(targets:arguments, options, messageOnly:ex => ex is OperationCanceledException || ex is WithoutStackException).ConfigureAwait(false);
            return 0;
        }
        catch (TargetFailedException targetException) {
            if (targetException.InnerException is OperationCanceledException || targetException.InnerException is WithoutStackException) {
                Console.WriteLine(Red(targetException.Message));
            }
            return 1;
        }
        catch (Exception ex) {
            Console.WriteLine(Red($"Unhandled exception: {ex}"));
            return 1;
        }


        static void SetEnvVariables()
        {
            Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
            Environment.SetEnvironmentVariable("DOTNET_SVCUTIL_TELEMETRY_OPTOUT", "1");
            Environment.SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
            Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "1");
            Environment.SetEnvironmentVariable("DOTNET_WATCH_RESTART_ON_RUDE_EDIT", "1");
            Environment.SetEnvironmentVariable("POWERSHELL_TELEMETRY_OPTOUT", "1");
            Environment.SetEnvironmentVariable("POWERSHELL_UPDATECHECK_OPTOUT", "1");
            Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en");
            Environment.SetEnvironmentVariable("UseRazorBuildServer", "false");
            Environment.SetEnvironmentVariable("UseSharedCompilation", "false");
            Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        }


        static void PrintHeader()
        {
            const string header = " /\\  _ _|_     _.|  _ |_  _._|_\n/--\\(_  |_ |_|(_||o(_ | |(_| |_";
            const double freq = 0.1;
            var rainbow = new Rainbow(freq);
            for (var i = 0; i < header.Length; i++) {
                Console.Write(rainbow.Next().Text(header[i].ToString()));
                if (header[i] == '\n')
                    rainbow = new Rainbow(freq);
            }
            Console.Write("\n");
        }
    }
}

internal class WithoutStackException : Exception
{
    public WithoutStackException() { }

    public WithoutStackException(string? message) : base(message)
    {
    }

    public WithoutStackException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public WithoutStackException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    protected WithoutStackException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
