using System.Diagnostics;
using static Crayon.Output;
using static Build.CliWrapCommandExtensions;
using static Build.Windows;

namespace Build;

public class ProcessWatch : IDisposable
{
    private Process Process { get; }
    private CancellationTokenSource CancellationTokenSource { get; }

    private ProcessWatch(Process process, CancellationTokenSource cancellationTokenSource)
    {
        CancellationTokenSource = cancellationTokenSource;
        Process = process;
    }

    public static ProcessWatch Start(
        string logPrefix,
        string exe,
        string args,
        string projectDir,
        Dictionary<string, string> envVars,
        CancellationTokenSource cancellationTokenSource)
    {
        try {
            Process process = new ();
            var psiDotnet = new ProcessStartInfo(exe, args) {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetFullPath(projectDir),
            };
            foreach (var envVar in envVars)
                psiDotnet.EnvironmentVariables[envVar.Key] = envVar.Value;

            process.StartInfo = psiDotnet;
            process.OutputDataReceived += (_, e) => {
                if (e.Data == null)
                    return;

                Console.WriteLine(Green($"{logPrefix}: ") + Colorize(e.Data));
            };
            process.ErrorDataReceived += (_, e) => {
                if (e.Data == null)
                    return;

                Console.WriteLine(Green($"{logPrefix}: ") + Red(e.Data));
            };
            process.Start();

            if (process.HasExited)
                throw new WithoutStackException("Can't start dotnet watch");

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            return new ProcessWatch(process, cancellationTokenSource);
        }
        catch {
            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();
            throw;
        }
    }

    public void Dispose()
    {
        KillProcessTree(Process);
        Process.Dispose();
    }

    public async Task WaitForExit()
    {
        try {
            await Process.WaitForExitAsync(CancellationTokenSource.Token);
        }
        finally {
            if (!CancellationTokenSource.IsCancellationRequested)
                CancellationTokenSource.Cancel();
        }
    }

    /// <seealso cref="Process.Kill(bool)"/> doesn't kill all child processes of npm.bat, this is workaround of that
    private static void KillProcessTree(Process process)
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
}
