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
        // -role:<role> argument
        var roleOverride = args
            .Select(x => x.OrdinalStartsWith(RoleArgPrefix) ? x[RoleArgPrefix.Length..].Trim() : null)
            .Select(HostRoles.Server.Parse)
            .SingleOrDefault(x => !x.IsNone);
        if (!roleOverride.IsNone) {
            WriteLine($"Role override: {roleOverride}");
            Environment.SetEnvironmentVariable(ServerRoleEnvVar, roleOverride.Value);
        }

        // -url:<url> argument
        var urlOverride = args
            .Select(x => x.OrdinalStartsWith(UrlArgPrefix) ? x[UrlArgPrefix.Length..].Trim() : null)
            .SingleOrDefault(x => !x.IsNullOrEmpty());
        if (!urlOverride.IsNullOrEmpty()) {
            WriteLine($"URL override: {urlOverride}");
            Environment.SetEnvironmentVariable(UrlsEnvVar, urlOverride);
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

        // Using similar host to get own http:// endpoint
        var similarHost = new AppHost().Build(configurationOnly: true);
        var endpoint = ServerEndpoints.List(similarHost.Services, "http://").FirstOrDefault();
        var (host, port) = ServerEndpoints.Parse(endpoint);

        var ownUrl = GetUrl(ownRoleIndex);
        WriteLine($"MultiHost mode. Own role: {ownRole} @ {ownUrl}");

        for (var roleIndex = 0; roleIndex < AllRoles.Length; roleIndex++) {
            var url = $"http://{host}:{port + roleIndex}";
            var role = AllRoles[roleIndex];
            if (role == ownRole)
                continue;

            LaunchAppHost(role, url);
        }

        // In the very end: set env. vars to own role vars
        Environment.SetEnvironmentVariable(UrlsEnvVar, ownUrl);
        Environment.SetEnvironmentVariable(ServerRoleEnvVar, ownRole.Value);

        string GetUrl(int roleIndex)
            => $"http://{host}:{port + roleIndex}";
    }

    // Private methods

    private static void LaunchAppHost(HostRole role, string url)
    {
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
