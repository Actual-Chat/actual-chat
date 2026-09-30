using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

namespace Build.Commands;

/// <summary>
/// Finds the newest APK artifact CI uploaded for an app id on a branch and downloads it
/// with <c>gh</c>, so <c>b app install android --ci</c> installs the binary CI shipped.
/// </summary>
internal static class AndroidCiApk
{
    public const string LatestReleaseBranch = "release/*";
    private const string Workflow = "build-test-deploy-dev.yml";
    private const string ReleaseBranchPrefix = "release/";
    private const int MaxRunCount = 30;
    // retention-days of the APK upload in build-test-deploy-dev.yml
    private const int ArtifactRetentionDays = 10;

    public static string GetApkPath(string appId)
        => Path.Combine("artifacts", "ci", appId, $"{appId}-Signed.apk");

    public static async Task<int> Download(string appId, string branch, CancellationToken cancellationToken)
    {
        var gh = Utils.FindGhExe();
        if (branch == LatestReleaseBranch)
            branch = await GetLatestReleaseBranch(gh, cancellationToken).ConfigureAwait(false);
        var (runId, artifactName) = await FindArtifact(gh, appId, branch, cancellationToken).ConfigureAwait(false);
        AnsiConsole.MarkupLine(
            $"[green]Found:[/] {artifactName.EscapeMarkup()} [grey](run {runId}, {branch.EscapeMarkup()})[/]");

        var dir = Path.GetDirectoryName(GetApkPath(appId))!;
        // gh run download refuses to overwrite the previous download
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        var result = await Cli.Wrap(gh)
            .WithArguments(["run", "download", runId, "-n", artifactName, "-D", dir])
            .ToConsole()
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken)
            .Task
            .ConfigureAwait(false);
        return result.ExitCode;
    }

    // Private methods

    private static async Task<string> GetLatestReleaseBranch(string gh, CancellationToken cancellationToken)
    {
        var refs = await GhApi(gh, "git/matching-refs/heads/" + ReleaseBranchPrefix, ".[].ref", cancellationToken)
            .ConfigureAwait(false);
        var branches = (
            from @ref in refs
            let branch = @ref["refs/heads/".Length..]
            let version = Version.TryParse(branch[ReleaseBranchPrefix.Length..].TrimStart('v'), out var v) ? v : null
            where version is not null
            select (Branch: branch, Version: version)
            ).ToList();
        if (branches.Count == 0)
            throw new WithoutStackException($"No {LatestReleaseBranch} branches found.");

        return branches.MaxBy(x => x.Version).Branch;
    }

    private static async Task<(string RunId, string ArtifactName)> FindArtifact(
        string gh, string appId, string branch, CancellationToken cancellationToken)
    {
        // Without "created", GitHub serves a busy branch's runs from a stale index - weeks-old ones first
        var minCreatedAt = DateTime.UtcNow.AddDays(-ArtifactRetentionDays).ToString("yyyy-MM-dd");
        var runsPath = $"actions/workflows/{Workflow}/runs?branch={Uri.EscapeDataString(branch)}"
            + $"&created={Uri.EscapeDataString(">=" + minCreatedAt)}&per_page={MaxRunCount}";
        var runIds = await GhApi(gh, runsPath, ".workflow_runs[].id", cancellationToken).ConfigureAwait(false);
        foreach (var runId in runIds) {
            var names = await GhApi(gh,
                    $"actions/runs/{runId}/artifacts",
                    ".artifacts[] | select(.expired | not) | .name",
                    cancellationToken)
                .ConfigureAwait(false);
            // Named "<appId>.<SemVer2>.apk"; "chat.actual.app." never prefixes a dev app artifact
            var name = names.FirstOrDefault(x => x.StartsWith(appId + ".") && x.EndsWith(".apk"));
            if (name is not null)
                return (runId, name);
        }
        throw new WithoutStackException(
            $"No {appId} APK among the {runIds.Length} {Workflow} runs on {branch}"
            + $" in the last {ArtifactRetentionDays} days.");
    }

    private static async Task<string[]> GhApi(string gh, string path, string jq, CancellationToken cancellationToken)
    {
        // gh fills {owner}/{repo} in from the current repository's remote
        var result = await Cli.Wrap(gh)
            .WithArguments(["api", "repos/{owner}/{repo}/" + path, "--jq", jq])
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken)
            .Task
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new WithoutStackException($"gh api {path} failed: {result.StandardError.Trim()}");

        return result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
