using ActualChat.Hosting;
using static System.Console;

namespace ActualChat.App.Server;

public static class CommandLineHandler
{
    private const string UrlArgPrefix = "-url:";
    private const string RoleArgPrefix = "-role:";
    private const string MultiHostRoleArgPrefix = "-multihost-role:";
    private const string UrlsEnvVar = "URLS";
    private const string ServerRoleEnvVar = "HostSettings__ServerRole";
    private static readonly HostRole[] AllRoles = [
        HostRole.FrontendServer,
        HostRole.BackendServer,
    ];

    public static void Process(string[] args)
    {
        // -url:<url> argument
        var urlOverride = args
            .Select(x => x.OrdinalStartsWith(UrlArgPrefix) ? x[UrlArgPrefix.Length..].Trim() : null)
            .SingleOrDefault(x => !x.IsNullOrEmpty());
        if (!urlOverride.IsNullOrEmpty()) {
            WriteLine($"URL override: {urlOverride}");
            Environment.SetEnvironmentVariable(UrlsEnvVar, urlOverride);
        }

        // -role:<role> argument
        var roleOverride = args
            .Select(x => x.OrdinalStartsWith(RoleArgPrefix) ? x[RoleArgPrefix.Length..].Trim() : null)
            .Select(HostRoles.Server.Parse)
            .SingleOrDefault(x => !x.IsNone);
        if (!roleOverride.IsNone) {
            WriteLine($"Role override: {roleOverride}");
            Environment.SetEnvironmentVariable(ServerRoleEnvVar, roleOverride.Value);
        }

        // "-multihost-role:<role>" argument
        var ownRole = args
            .Select(x => x.OrdinalStartsWith(MultiHostRoleArgPrefix) ? x[MultiHostRoleArgPrefix.Length..].Trim().NullIfEmpty() : null)
            .Select(HostRoles.Server.Parse)
            .SingleOrDefault(x => !x.IsNone);
        if (ownRole.IsNone)
            return;

        var ownRoleIndex = Array.IndexOf(AllRoles, ownRole);
        if (ownRoleIndex < 0)
            throw StandardError.Configuration($"Invalid {MultiHostRoleArgPrefix} argument value.");

        var (host, defaultPort) = GetDefaultHostAndPort();
        var ownUrl = GetUrl(ownRoleIndex);
        WriteLine($"MultiHost mode. Own role: {ownRole} @ {ownUrl}");

        for (var roleIndex = 0; roleIndex < AllRoles.Length; roleIndex++) {
            var role = AllRoles[roleIndex];
            if (role == ownRole)
                continue;

            LaunchAppHost(role, host, defaultPort + roleIndex);
        }

        // In the very end: set env. vars to own role vars
        Environment.SetEnvironmentVariable(UrlsEnvVar, ownUrl);
        Environment.SetEnvironmentVariable(ServerRoleEnvVar, ownRole.Value);

        if (args.Any(x => OrdinalEquals(x, "-kb")))
            _ = WatchKeyboard();

        string GetUrl(int roleIndex)
            => $"http://{host}:{defaultPort + roleIndex}";

        async Task WatchKeyboard() {
            var port = defaultPort + AllRoles.Length;
            while (true) {
                if (!KeyAvailable) {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                var key = ReadKey(true).KeyChar;
                if (key is < '0' or > '9')
                    continue;

                var role = AllRoles.GetValueOrDefault(key - '0');
                if (role.IsNone || role == HostRole.FrontendServer)
                    continue;

                LaunchAppHost(role, host, port++);
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }

    // Private methods

    private static (string Host, int Port) GetDefaultHostAndPort()
    {
        // Building a similar host to get our own http:// endpoint
        var similarHost = new AppHost().Build(configurationOnly: true);
        var endpoint = ServerEndpoints.List(similarHost.Services, "http://").FirstOrDefault();
        return ServerEndpoints.Parse(endpoint);
    }

    private static void LaunchAppHost(HostRole role, string host, int port)
    {
        var url = $"http://{host}:{port}";
        WriteLine($"Launching host: {role} @ {url}");
        var startInfo = new ProcessStartInfo("cmd.exe") {
            ArgumentList = {
                "/C", "start", "/D", Environment.CurrentDirectory, Environment.ProcessPath!,
                RoleArgPrefix + role.Value,
                UrlArgPrefix + url,
            },
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(startInfo);
    }
}
