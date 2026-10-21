using ActualChat.Testing.ApiContracts;
using ActualChat.Testing.Host;
using ActualLab.IO;
using ActualLab.Rpc;

namespace ActualChat.Core.Server.IntegrationTests;

/// <summary>
/// Checks the server against the API contract of every released client in <c>data/api-contract</c>:
/// each wire id still resolves, and both sides can read what the other sends. A contract is taken on
/// the release branch by running this with <c>ActualChat_WriteApiContract=true</c>.
/// </summary>
[Collection(nameof(ServerCollection))]
public sealed class ApiContractTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ExceptionsFileName = "exceptions.txt";

    [Fact]
    public void EveryReleasedClientShouldStillBeServed()
    {
        // arrange
        var registry = AppHost.Services.RpcHub().ServiceRegistry;
        var dir = ContractDir();
        if (MustWrite()) {
            var saved = ApiContractBuilder.Build(registry, ApiConstants.Version);
            var path = dir & $"v{saved.Version.ToString(2)}.txt";
            File.WriteAllText(path, saved.Format());
            Out.WriteLine($"Written: {path} ({saved.Methods.Count} methods, {saved.Types.Count} types)");
            return;
        }

        var current = ApiContractBuilder.Build(registry, ApiConstants.Version, mustIncludeLegacy: true);
        var contracts = Directory.GetFiles(dir, "v*.txt")
            .Select(f => ApiContract.Parse(File.ReadAllText(f)))
            .Where(c => c.Version <= current.Version)
            .OrderBy(c => c.Version)
            .ToList();
        var unsupported = contracts
            .Where(c => c.Version < SystemProperties.MinCompatibleVersion)
            .Select(c => c.Version.ToString(2))
            .ToList();
        var exceptions = ReadExceptions(dir & ExceptionsFileName);

        // act
        var problems = contracts.SelectMany(c => Check(registry, c, current)).ToList();
        var unexcused = problems
            .Where(p => !exceptions.Any(e => IsExcused(p.Key, e)))
            .Select(p => p.Text)
            .ToList();
        var unusedExceptions = exceptions.Where(e => !problems.Any(p => IsExcused(p.Key, e))).ToList();

        // assert
        Out.WriteLine($"Checked: {string.Join(", ", contracts.Select(c => c.Version.ToString(2)))}");
        foreach (var problem in problems)
            Out.WriteLine(problem.Text);
        contracts.Should().NotBeEmpty("data/api-contract must hold the contracts of the released clients");
        unsupported.Should().BeEmpty(
            "a client below SystemProperties.MinCompatibleVersion ({0}) is no longer served - delete its "
            + "contract and its lines in {1}",
            SystemProperties.MinCompatibleVersion.ToString(2), ExceptionsFileName);
        unexcused.Should().BeEmpty(
            "a released client must still be served - fix it, or list the method in {0} with the reason "
            + "it may break:\n{1}",
            ExceptionsFileName, string.Join("\n", unexcused));
        unusedExceptions.Should().BeEmpty(
            "an exception in {0} that matches no problem is stale:\n{1}",
            ExceptionsFileName, string.Join("\n", unusedExceptions));
    }

    // Private methods

    private static IEnumerable<(string Key, string Text)> Check(
        RpcServiceRegistry registry,
        ApiContract contract,
        ApiContract current)
    {
        // The resolver gets the version a client of that release reports in the RPC handshake
        var version = contract.Version.ToString(2);
        var resolver = registry.GetServerMethodResolver(new VersionSet($"Api={version}.0.0"));
        var checker = new ApiContractChecker(contract, current, hashCode => resolver[hashCode]?.FullName);
        return checker.Check().Select(p => ($"{version} {p.Method} {p.Location}", $"{version} {p}"));
    }

    private static List<string> ReadExceptions(FilePath path)
        // "<version> <method> [<location>] - <why it may break>"; the reason is for the reader only
        => File.Exists(path)
            ? File.ReadAllLines(path)
                .Where(x => !x.IsNullOrWhiteSpace() && !x.StartsWith('#'))
                .Select(x => x.Split(" - ", 2)[0].Trim())
                .ToList()
            : [];

    private static bool IsExcused(string problemKey, string exception)
        // An exception names a method, or a location in it: "result.SinceAt[6]" also covers what's below
        => problemKey.StartsWith(exception)
            && (problemKey.Length == exception.Length || problemKey[exception.Length] is ' ' or '.' or '[' or '<');

    private static bool MustWrite()
        => Environment.GetEnvironmentVariable("ActualChat_WriteApiContract") is "true" or "1";

    private static FilePath ContractDir([CallerFilePath] string? callerFilePath = null)
        => FilePath.New(callerFilePath).DirectoryPath & "data" & "api-contract";
}
