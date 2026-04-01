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
        CancellationTokenSource cancellationTokenSource,
        string? logFile = null)
    {
        try {
            RollingLogWriter? logWriter = null;
            if (logFile != null) {
                var dir = Path.GetDirectoryName(logFile);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                logWriter = new RollingLogWriter(logFile);
            }

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
                logWriter?.WriteLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => {
                if (e.Data == null)
                    return;

                Console.WriteLine(Green($"{logPrefix}: ") + Red(e.Data));
                logWriter?.WriteLine(e.Data);
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
            await Process.WaitForExitAsync(CancellationTokenSource.Token).ConfigureAwait(false);
        }
        finally {
            if (!CancellationTokenSource.IsCancellationRequested)
                await CancellationTokenSource.CancelAsync().ConfigureAwait(false);
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

/// <summary>
/// Writes to a log file, truncating from the beginning when it exceeds maxSize.
/// On rotation, the current file is moved to .prev and a fresh file is started.
/// </summary>
internal sealed class RollingLogWriter(string path, long maxSize = 512 * 1024)
{
    private readonly string _prevPath = path + ".prev";
    private readonly Lock _lock = new();
    private StreamWriter _writer = new (path, append: false) { AutoFlush = true };
    private long _bytesWritten = 0;

    public void WriteLine(string line)
    {
        lock (_lock) {
            _writer.WriteLine(line);
            _bytesWritten += line.Length + Environment.NewLine.Length;
            if (_bytesWritten >= maxSize)
                Rotate();
        }
    }

    private void Rotate()
    {
        _writer.Dispose();
        try { File.Delete(_prevPath); } catch { }
        try { File.Move(path, _prevPath); } catch { }
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        _bytesWritten = 0;
    }
}
