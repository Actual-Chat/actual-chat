using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
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
using static Build.WinApi;
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

            if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
                throw new WithoutStackException("Use dotnet watch + webpack watch without build system, " +
                    "because the workaround of https://github.com/dotnet/aspnetcore/issues/37190 only works on Windows");
            }

            var npm = TryFindCommandPath("npm")
                ?? throw new WithoutStackException(new FileNotFoundException("'npm' command isn't found. Install nodejs from https://nodejs.org/"));

            const string pipeName = "buildstep-watch";
            // if one of process exits then close another on Cancel()
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // cliwrap doesn't kill childs of the process..
            // https://github.com/Tyrrrz/CliWrap/blob/49f34ad145501da2d5381058a9ab8d336e788511/CliWrap/Utils/Polyfills.cs#L117
            using Process dotnetProcess = new();
            using Process npmProcess = new();
            try {
                // any other than `dotnet watch run` command will use legacy ho-reload behavior (profile), so it's better to use the default watch running args
                // CliWrap doesn't give us process object, which is needed for the workaround of https://github.com/dotnet/aspnetcore/issues/37190

                var psiDotnet = new ProcessStartInfo("cmd.exe", $"/c \"" +
                "title watch " +
                "&& set ASPNETCORE_ENVIRONMENT=Development " +
                "&& set MSBUILDDISABLENODEREUSE=1 " +
                "&& set EnableAnalyzer=false " +
                "&& set EnableNETAnalyzers=false " +
                $"&& dotnet watch -v run\" >\\\\.\\pipe\\{pipeName}") {
                    UseShellExecute = true,
                    // ConsoleKey event doesn't work with a nointeractive window, so we have to hide the window after process start
                    WindowStyle = ProcessWindowStyle.Normal,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(Path.Combine("src", "dotnet", "Host")),
                };
                dotnetProcess.StartInfo = psiDotnet;
                _ = ReadNamedPipeAsync(dotnetProcess, pipeName, cts.Token);
                dotnetProcess.Start();
                if (dotnetProcess.HasExited) {
                    throw new WithoutStackException("Can't start dotnet watch");
                }
                _ = ShowWindow(dotnetProcess.MainWindowHandle, SW_HIDE);
                _ = Task.Delay(500).ContinueWith(t => _ = ShowWindow(dotnetProcess.MainWindowHandle, SW_HIDE));

                var psiNpm = new ProcessStartInfo(npm, "run watch") {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(Path.Combine("src", "nodejs")),
                };
                psiNpm.EnvironmentVariables.Add("CI", "true");
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
                if (!process.HasExited && process.Id != 0) {
                    try {
                        var childs = GetChildProcesses((uint)process.Id);
                        process.Kill(entireProcessTree: true);
                        foreach (var child in childs) {
                            try {
                                var childProcess = Process.GetProcessById((int)child.ProcessId);
                                if (childProcess.Id != 0 && !childProcess.HasExited) {
                                    childProcess.Kill(entireProcessTree: true);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        });

        ///<summary> the part of workaround of <see href="https://github.com/dotnet/aspnetcore/issues/37190" /> </summary>
        static async Task ReadNamedPipeAsync(Process process, string pipeName, CancellationToken cancellationToken)
        {
            try {
                using var server = new NamedPipeServerStream(pipeName);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                bool? isAlwaysApplied = null;
                var prefix = Green("dotnet: ");
                while (!cancellationToken.IsCancellationRequested && server.IsConnected) {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) {
                        if (process.HasExited) {
                            break;
                        }
                        continue;
                    }
                    Console.WriteLine(prefix + Colorize(line));
                    /// <see href="https://github.com/dotnet/sdk/blob/3860d6e404bc7fc08ca55ca158fed212ccee12ad/src/BuiltInTools/dotnet-watch/HotReload/RudeEditPreference.cs#L34" />

                    if (isAlwaysApplied == null && line.Contains("Do you want to restart your app - Yes (y) / No (n) / Always (a) / Never (v)?", StringComparison.OrdinalIgnoreCase)) {
                        isAlwaysApplied = false;
                        Console.WriteLine(Red("[BuildSystem] ") + "Found user prompt...");

                        _ = Task.Factory.StartNew(() => {
                            while (isAlwaysApplied == false) {
                                Console.WriteLine(Red("[BuildSystem] ") + "Send 'a' as always...");
                                SendAlways(process);
                                Thread.Sleep(300);
                            }
                        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                    else if (isAlwaysApplied == false) {
                        Console.WriteLine(Red("[BuildSystem] ") + "Found a new line, disable sending...");
                        isAlwaysApplied = true;
                    }

                }
            }
            catch (Exception ex) {
                Console.WriteLine("[BuildSystem] Prompt workaround error: " + ex.ToString());
            }

            static void SendAlways(Process process)
            {
                var hwnd = process.MainWindowHandle;
                _ = PostMessage(hwnd, WM_KEYDOWN, VK_A, 0);
                Thread.Sleep(200);
                _ = PostMessage(hwnd, WM_KEYUP, VK_A, 0);
                Thread.Sleep(500);
                _ = PostMessage(hwnd, WM_KEYDOWN, VK_RETURN, 0);
                Thread.Sleep(200);
                _ = PostMessage(hwnd, WM_KEYUP, VK_RETURN, 0);
            }
        }

        Target("clean-dist", () => {
            var extensionDir = Path.Combine("src", "dotnet", "UI.Blazor.Host", "wwwroot", "dist");
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
            var resultsDirectory = Path.GetFullPath(Path.Combine("artifacts", "tests", "output"));
            if (!Directory.Exists(resultsDirectory))
                Directory.CreateDirectory(resultsDirectory);
            var cmd = await Cli.Wrap(dotnet)
                .WithArguments("test " +
                "--nologo " +
                "--filter \"FullyQualifiedName~UnitTests\" " +
                "--no-restore " +
                "--blame-hang " +
                "--blame-hang-timeout 60s " +
                $"--results-directory {resultsDirectory} " +
                "--logger:console;verbosity=detailed " +
                $"--logger:trx;LogFileName=\"{Path.Combine(resultsDirectory, "unit.trx").Replace("\"", "\\\"")}\" " +
                $"-c {configuration} "
                )
                .ToConsole()
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);

            MoveAttachmentsToResultsDirectory(resultsDirectory, cmd.StandardOutput);
            TryRemoveTestsOutputDirectories(resultsDirectory);
        });

        Target("generate-version", DependsOn("restore-tools"), async () => {
            var cmd = await Cli.Wrap(dotnet)
                .WithArguments("nbgv get-version --format json")
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);
            var nbgv = JsonSerializer.Deserialize<NbgvModel>(cmd.StandardOutput ?? throw new WithoutStackException("nbgv returned null"));

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

        Target("integration-tests", DependsOn("restore-tools"), async () => {
            var resultsDirectory = Path.GetFullPath(Path.Combine("artifacts", "tests", "output"));
            if (!Directory.Exists(resultsDirectory))
                Directory.CreateDirectory(resultsDirectory);
            var cmd = await Cli.Wrap(dotnet)
                .WithArguments($"test " +
                "--nologo " +
                "--filter \"FullyQualifiedName~IntegrationTests\" " +
                "--no-restore " +
                "--blame-hang " +
                "--blame-hang-timeout 300s " +
                $"--results-directory {resultsDirectory} " +
                "--logger:console;verbosity=detailed " +
                $"--logger:trx;LogFileName=\"{Path.Combine(resultsDirectory, "integration.trx").Replace("\"", "\\\"")}\" " +
                $"-c {configuration} "
                )
                .ToConsole()
                .ExecuteBufferedAsync(cancellationToken).Task.ConfigureAwait(false);

            MoveAttachmentsToResultsDirectory(resultsDirectory, cmd.StandardOutput);
            TryRemoveTestsOutputDirectories(resultsDirectory);
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
                    .WithArguments($"build -noLogo -maxCpuCount -nodeReuse:false -c {configuration}")
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
        });

        Target("restore", async () => {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try {
                await Cli.Wrap(dotnet).WithArguments($"msbuild -noLogo " +
                        "-t:Restore " +
                        "-p:RestoreForce=true " +
                        "-maxCpuCount " +
                        "-nodeReuse:false " +
                        "-p:UseRazorBuildServer=false " +
                        "-p:UseSharedCompilation=false " +
                        "-p:EnableAnalyzer=false " +
                        "-p:EnableNETAnalyzers=false " +
                        $"-p:Configuration={configuration}"
                    ).ToConsole(Green("dotnet restore: "))
                    .ExecuteAsync(cts.Token).Task.ConfigureAwait(false);
                ;
            }
            finally {
                cts.Cancel();
                cts.Dispose();
            }
        });

        Target("default", DependsOn("build"));

        try {
            /// <see cref="RunTargetsAndExitAsync"/> will hang Target on ctrl+c
            await RunTargetsWithoutExitingAsync(arguments, options, ex => ex is OperationCanceledException || ex is WithoutStackException).ConfigureAwait(false);
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
            Environment.SetEnvironmentVariable("POWERSHELL_TELEMETRY_OPTOUT", "1");
            Environment.SetEnvironmentVariable("POWERSHELL_UPDATECHECK_OPTOUT", "1");
            Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "en");
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

    // Removes all files in inner folders, workaround of https://github.com/microsoft/vstest/issues/2334
    private static void TryRemoveTestsOutputDirectories(string resultsDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(resultsDirectory)) {
            try {
                Directory.Delete(directory, recursive: true);
            }
            catch { }
        }
    }

    // Removes guid from tests output path, workaround of https://github.com/microsoft/vstest/issues/2378
    private static void MoveAttachmentsToResultsDirectory(string resultsDirectory, string output)
    {
        var attachmentsRegex = new Regex($@"Attachments:(?<filepaths>(?<filepath>[\s]+[^\n]+{Regex.Escape(resultsDirectory)}[^\n]+[\n])+)", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        foreach (var match in attachmentsRegex.Matches(output).Cast<Match>()) {
            var regexPaths = match.Groups["filepaths"].Value.Trim('\n', ' ', '\t', '\r');
            var paths = regexPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            if (paths.Length > 0) {
                foreach (var path in paths) {
                    var filename = Path.GetFileNameWithoutExtension(path);
                    var extension = Path.GetExtension(path);
                    string newPath = null!;
                    int index = 0;
                    do {
                        newPath = Path.Combine(resultsDirectory, $"{filename}.{index++}{extension}");
                    }
                    while (File.Exists(newPath));
                    Console.WriteLine(Underline(Magenta($"Moving: {path} -> {newPath}")));
                    File.Move(path, newPath, overwrite: true);
                }
                Directory.Delete(Path.GetDirectoryName(paths[0])!, true);
            }

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
