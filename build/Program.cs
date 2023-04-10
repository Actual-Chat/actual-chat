using System.Diagnostics;
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
using static Build.CliWrapCommandExtensions;
using static Build.Windows;
using static Bullseye.Targets;
using static Crayon.Output;

namespace Build;


// DragonFruit approach doesn't the best option here (for example currently it doesn't support aliases),
// but it's clean and works, so I've decided to use it.
internal static class Program
{
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

        var dotnet = TryFindDotNetExePath()
            ?? throw new FileNotFoundException("'dotnet' command isn't found. Try to set DOTNET_ROOT variable.");

        Target("watch", DependsOn("clean-dist"), async () => {

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                throw new WithoutStackException($"Watch is not implemented for '{RuntimeInformation.OSDescription}'. Use dotnet watch + webpack watch without build system");
            }

            var npm = TryFindCommandPath("npm")
                ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

            // if one of process exits then close another on Cancel()
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // cliwrap doesn't kill children of the process..
            // https://github.com/Tyrrrz/CliWrap/blob/49f34ad145501da2d5381058a9ab8d336e788511/CliWrap/Utils/Polyfills.cs#L117
            using Process dotnetProcess = new();
            using Process npmProcess = new();
            try {
                // any other than `dotnet watch run` command will use legacy ho-reload behavior (profile), so it's better to use the default watch running args
                // CliWrap doesn't give us process object, which is needed for the workaround of https://github.com/dotnet/aspnetcore/issues/37190
                var psiDotnet = new ProcessStartInfo(dotnet, "watch -v run") {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(Path.Combine("src", "dotnet", "App.Server")),
                    EnvironmentVariables = {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development",
                        ["EnableAnalyzer"] = "false",
                        ["EnableNETAnalyzers"] = "false"
                    }
                };
                dotnetProcess.StartInfo = psiDotnet;
                dotnetProcess.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
                    if (e?.Data == null)
                        return;
                    Console.WriteLine(Green("dotnet: ") + Colorize(e.Data));
                };
                dotnetProcess.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => {
                    if (e?.Data == null)
                        return;
                    Console.WriteLine(Green("dotnet: ") + Red(e.Data));
                };
                dotnetProcess.Start();

                if (dotnetProcess.HasExited) {
                    throw new WithoutStackException("Can't start dotnet watch");
                }
                dotnetProcess.BeginErrorReadLine();
                dotnetProcess.BeginOutputReadLine();

                var psiNpm = new ProcessStartInfo(npm, "run watch") {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(Path.Combine("src", "nodejs")),
                    EnvironmentVariables = {
                        ["CI"] = "true"
                    }
                };
                npmProcess.StartInfo = psiNpm;
                npmProcess.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
                    if (e?.Data == null)
                        return;
                    Console.WriteLine(Blue("webpack: ") + e.Data);
                };
                npmProcess.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => {
                    if (e?.Data == null)
                        return;
                    Console.WriteLine(Blue("webpack: ") + Red(e.Data));
                };
                npmProcess.Start();
                if (npmProcess.HasExited) {
                    throw new WithoutStackException("Can't start webpack watch");
                }
                npmProcess.BeginErrorReadLine();
                npmProcess.BeginOutputReadLine();
                var npmTask = npmProcess.WaitForExitAsync(cts.Token);
                var dotnetTask = dotnetProcess.WaitForExitAsync(cts.Token);
                await Task.WhenAny(npmTask, dotnetTask).ConfigureAwait(false);
            }
            finally {
                KillProcessTree(dotnetProcess);
                KillProcessTree(npmProcess);
                if (!cts.IsCancellationRequested)
                    cts.Cancel();

                cts.Dispose();
            }
            Console.WriteLine(Yellow("The watching is over"));

            /// <seealso cref="Process.Kill(bool)"/> doesn't kill all child processes of npm.bat, this is workaround of that
            static void KillProcessTree(Process process)
            {
                try {
                    if (!process.HasExited && process.Id != 0) {
                        var children = GetChildProcesses((uint)process.Id);
                        process.Kill(entireProcessTree: true);
                        foreach (var child in children) {
                            try {
                                var childProcess = Process.GetProcessById((int)child.ProcessId);
                                if (childProcess.Id != 0 && !childProcess.HasExited) {
                                    childProcess.Kill(entireProcessTree: true);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

        });

        Target("clean-dist", () => {
            var extensionDir = Path.Combine("src", "dotnet", "App.Wasm", "wwwroot", "dist");
            if (Directory.Exists(extensionDir)) {
                Directory.Delete(extensionDir, recursive: true);
            }
        });

        Target("npm-install", async () => {
            var nodeModulesDir = Path.Combine("src", "nodejs", "node_modules");
            if (!Directory.Exists(nodeModulesDir)) {
                var npm = TryFindCommandPath("npm")
                    ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

                await Cli
                    .Wrap(npm)
                    .WithArguments("ci")
                    .WithWorkingDirectory(Path.Combine("src", "nodejs"))
                    .WithEnvironmentVariables(new Dictionary<string, string?>(1) { ["CI"] = "true" })
                    .ToConsole(Blue("npm install: "))
                    .ExecuteAsync(cancellationToken).Task.ConfigureAwait(false);
            }
        });

        Target("unit-tests", async () => {
            await Cli.Wrap(dotnet)
                .WithArguments("test " +
                "ActualChat.sln " +
                "--nologo " +
                "--filter \"FullyQualifiedName~UnitTests\" " +
                "--no-restore " +
                "--blame-hang " +
                "--blame-hang-timeout 60s " +
                "--logger \"console;verbosity=detailed\" " +
                "--logger \"trx;LogFileName=Results.trx\" " +
                (IsGitHubActions() ? "--logger GitHubActions " : "") +
                $"-c {configuration} "
                )
                .ToConsole()
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);
        });

        Target("generate-version", async () => {
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

        Target("integration-tests", async () => {
            await Cli.Wrap(dotnet)
                .WithArguments("test " +
                "ActualChat.sln " +
                "--nologo " +
                "--filter \"FullyQualifiedName~IntegrationTests&FullyQualifiedName!~UI.Blazor.IntegrationTests\" " +
                "--no-restore " +
                "--blame-hang " +
                "--blame-hang-timeout 300s " +
                "--logger \"console;verbosity=detailed\" " +
                "--logger \"trx;LogFileName=Results.trx\" " +
                (IsGitHubActions() ? "--logger GitHubActions " : "") +
                $"-c {configuration} "
                )
                .ToConsole()
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);
        });

        Target("clean-tests", () => {
            var extensionDir = Path.Combine("artifacts", "tests", "output");
            if (Directory.Exists(extensionDir)) {
                Directory.Delete(extensionDir, recursive: true);
            }
        });

        Target("tests", DependsOn("clean-tests", "unit-tests", "integration-tests"), () => { });

        Target("build", DependsOn("clean-dist", "npm-install"), async () => {
            var npm = TryFindCommandPath("npm")
                ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

            // if one of process exits then close another on Cancel()
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cts.Token;
            try {
                var dotnetTask = Cli
                    .Wrap(dotnet)
                    .WithArguments($"build ActualChat.sln -noLogo -maxCpuCount -nodeReuse:false -c {configuration}")
                    .ToConsole(Green("dotnet: "))
                    .ExecuteAsync(token).Task;

                var npmTask = Cli
                    .Wrap(npm)
                    .WithArguments($"run build:{configuration}")
                    .WithWorkingDirectory(Path.Combine("src", "nodejs"))
                    .WithEnvironmentVariables(new Dictionary<string, string?>(1) { ["CI"] = "true" })
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

        Target("maui", DependsOn("clean-dist", "npm-install"), async () => {
            var npm = TryFindCommandPath("npm")
                ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

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

                var npmTask = Cli
                    .Wrap(npm)
                    .WithArguments($"run build:{configuration}")
                    .WithWorkingDirectory(Path.Combine("src", "nodejs"))
                    .WithEnvironmentVariables(new Dictionary<string, string?>(1) { ["CI"] = "true" })
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

        Target("restore-tools", async () => {
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

        Target("restore", async () => {
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

        Target("default", DependsOn("build"));

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

    /// <summary>
    /// Returns full path for short commands like "npm" (on windows it will be 'C:\Program Files\nodejs\npm.cmd' for example)
    /// or null if full path not found
    /// </summary>
    internal static string? TryFindCommandPath(string cmd)
    {
        if (File.Exists(cmd)) {
            return Path.GetFullPath(cmd);
        }

        var values = Environment.GetEnvironmentVariable("PATH");
        if (values == null)
            return null;

        var isWindows = Environment.OSVersion.Platform != PlatformID.Unix;

        foreach (var path in values.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var fullPath = Path.Combine(path, cmd);
            if (isWindows) {
                if (File.Exists(fullPath + ".exe"))
                    return fullPath + ".exe";
                else if (File.Exists(fullPath + ".cmd"))
                    return fullPath + ".cmd";
                else if (File.Exists(fullPath + ".bat"))
                    return fullPath + ".bat";
            }
            else {
                if (File.Exists(fullPath + ".sh"))
                    return fullPath + ".sh";
            }
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    private static string? TryFindDotNetExePath()
    {
        var dotnet = "dotnet";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            dotnet += ".exe";

        var mainModule = Process.GetCurrentProcess().MainModule;
        if (!string.IsNullOrEmpty(mainModule?.FileName) && Path.GetFileName(mainModule.FileName)!.Equals(dotnet, StringComparison.OrdinalIgnoreCase))
            return mainModule.FileName;

        var environmentVariable = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(environmentVariable))
            return Path.Combine(environmentVariable, dotnet);

        var paths = Environment.GetEnvironmentVariable("PATH");
        if (paths == null)
            return null;

        foreach (var path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var fullPath = Path.Combine(path, dotnet);
            if (File.Exists(fullPath))
                return fullPath;
        }
        return null;
    }

    private static bool IsGitHubActions()
        => bool.TryParse(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), out bool isGitHubActions) && isGitHubActions;
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
