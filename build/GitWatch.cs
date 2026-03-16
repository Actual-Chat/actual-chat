using System.Diagnostics;
using static Crayon.Output;

namespace Build;

public class GitWatch : IDisposable
{
    private readonly string _mode;
    private readonly int _intervalSeconds;
    private readonly CancellationTokenSource _cts;
    private readonly Task _task;

    private GitWatch(string mode, int intervalSeconds, CancellationTokenSource cts)
    {
        _mode = mode;
        _intervalSeconds = intervalSeconds;
        _cts = cts;
        _task = RunAsync();
    }

    public static GitWatch? Start(string? mode, CancellationTokenSource cts, int intervalSeconds = 15)
    {
        if (string.IsNullOrEmpty(mode))
            return null;

        if (!mode.Equals("pull", StringComparison.OrdinalIgnoreCase)
            && !mode.Equals("reset", StringComparison.OrdinalIgnoreCase))
            throw new WithoutStackException($"Invalid --git-sync mode '{mode}'. Expected: pull or reset.");

        return new GitWatch(mode, intervalSeconds, cts);
    }

    public Task WaitForExit() => _task;

    public void Dispose() { }

    private async Task RunAsync()
    {
        var token = _cts.Token;
        var branch = await GetCurrentBranch(token).ConfigureAwait(false);
        if (branch == null) {
            Console.WriteLine(Red("git: ") + "failed to get current branch");
            return;
        }

        Console.WriteLine(Magenta("git: ") + $"mode={_mode}, branch={branch}, interval={_intervalSeconds}s");

        while (!token.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }

            branch = await GetCurrentBranch(token).ConfigureAwait(false) ?? branch;

            try {
                if (_mode.Equals("pull", StringComparison.OrdinalIgnoreCase))
                    await DoPull(token).ConfigureAwait(false);
                else if (_mode.Equals("reset", StringComparison.OrdinalIgnoreCase))
                    await DoReset(branch, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (Exception ex) {
                Console.WriteLine(Red("git: ") + ex.Message);
            }
        }
    }

    private static async Task DoPull(CancellationToken token)
    {
        var (exitCode, output) = await RunGit("pull", token).ConfigureAwait(false);
        if (exitCode != 0) {
            Console.WriteLine(Red("git: ") + "pull failed");
            Console.WriteLine(Red("git: ") + output);
        }
        else if (!output.Contains("Already up to date", StringComparison.Ordinal)) {
            Console.WriteLine(Magenta("git: ") + "pulled");
        }
    }

    private static async Task DoReset(string branch, CancellationToken token)
    {
        var (fetchExit, _) = await RunGit("fetch origin", token).ConfigureAwait(false);
        if (fetchExit != 0) {
            Console.WriteLine(Red("git: ") + "fetch failed");
            return;
        }

        var (_, localHash) = await RunGit("rev-parse HEAD", token).ConfigureAwait(false);
        var (remoteExit, remoteHash) = await RunGit($"rev-parse origin/{branch}", token).ConfigureAwait(false);
        if (remoteExit != 0) {
            Console.WriteLine(Red("git: ") + $"origin/{branch} not found");
            return;
        }

        if (!string.Equals(localHash.Trim(), remoteHash.Trim(), StringComparison.Ordinal)) {
            Console.WriteLine(Yellow("git: ") + $"resetting to origin/{branch}");
            await RunGit($"reset --hard origin/{branch}", token).ConfigureAwait(false);
        }
    }

    private static async Task<string?> GetCurrentBranch(CancellationToken token)
    {
        var (exitCode, output) = await RunGit("rev-parse --abbrev-ref HEAD", token).ConfigureAwait(false);
        return exitCode == 0 ? output.Trim() : null;
    }

    private static async Task<(int ExitCode, string Output)> RunGit(string args, CancellationToken token)
    {
        var psi = new ProcessStartInfo("git", args) {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return (process.ExitCode, stdout + stderr);
    }
}
