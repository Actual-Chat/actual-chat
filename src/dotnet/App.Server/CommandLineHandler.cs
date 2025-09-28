using ActualChat.Hosting;
using ActualChat.Redis.Module;
using static System.Console;

namespace ActualChat.App.Server;

public static class CommandLineHandler
{
    private const string UrlArgPrefix = "-url:";
    private const string RoleArgPrefix = "-role:";
    private const string KeyboardArg = "-kb";
    private const string ForceDistributedArg = "-distributed";
    private const string MultiHostRoleArgPrefix = "-multihost-role:";
    private const string RoleGroupDelimiter = ":";
    private const string UrlsEnvVar = "URLS";
    private const string ServerRoleEnvVar = "HostSettings__ServerRole";
    private const string MeshLockSubspaceEnvVar = "RedisSettings__MeshLockSubspace";
    private const string MeshLockOptionsPresetEnvVar = "RedisSettings__MeshLockOptionsPreset";

    private static readonly Dictionary<Symbol, HostRole[]> AllRoleGroups = new() {
        { "1", [HostRole.OneServer] },
        { "2", [HostRole.OneApiServer, HostRole.OneBackendServer] },
    };

    private static bool UseKeyboard { get; set; }
    private static bool ForceDistributed { get; set; }
    private static Symbol RoleGroupName { get; set; } = "1";
    private static int OwnRoleIndex { get; set; } = -1;
    private static HostRole[] RoleGroup => AllRoleGroups[RoleGroupName];
    private static HostRole OwnRole => RoleGroup[OwnRoleIndex];

    public static void Process(string[] args)
    {
        // -kb argument
        UseKeyboard = args.Any(x => OrdinalEquals(x, KeyboardArg));
        // -distributed argument
        ForceDistributed = HostRolesExt.ForceDistributedModeForServerModeServices = args.Any(x => OrdinalEquals(x, ForceDistributedArg));

        // -url:<url> argument
        var urlOverride = args
            .Select(x => x.OrdinalStartsWith(UrlArgPrefix) ? x[UrlArgPrefix.Length..].Trim() : null)
            .SingleOrDefault(x => !x.IsNullOrEmpty());
        if (!urlOverride.IsNullOrEmpty()) {
            WriteLine($"URL override: {urlOverride}");
            Environment.SetEnvironmentVariable(UrlsEnvVar, urlOverride);
        }

        // -role:<role-group>:<own-role> argument
        if (TryParseRoleArgument(args, RoleArgPrefix)) {
            if (TryParseRoleArgument(args, MultiHostRoleArgPrefix))
                throw StandardError.CommandLine($"{RoleArgPrefix} and {MultiHostRoleArgPrefix} can't be used together.");

            var meshLockSubspace = Environment.GetEnvironmentVariable(MeshLockSubspaceEnvVar);
            if (meshLockSubspace.IsNullOrEmpty())
                throw StandardError.CommandLine($"{RoleArgPrefix} requires '{MeshLockSubspaceEnvVar}' env. var.");

            var meshLockOptionsPreset = Environment.GetEnvironmentVariable(MeshLockOptionsPresetEnvVar);
            if (meshLockOptionsPreset.IsNullOrEmpty())
                throw StandardError.CommandLine($"{RoleArgPrefix} requires '{MeshLockOptionsPresetEnvVar}' env. var.");

            WriteLine($"Role override: {OwnRole} of role group '{RoleGroupName}'.");
            WriteLine($"IMeshLocks settings: '{meshLockSubspace}' subspace, '{meshLockOptionsPreset}' options preset");
            Environment.SetEnvironmentVariable(ServerRoleEnvVar, OwnRole.Value);
            StartKeyboardWatcher();
            return;
        }

        // -multihost-role:<role-group>:<own-role> argument
        if (TryParseRoleArgument(args, MultiHostRoleArgPrefix)) {
            var redisSettings = ConfigOnlyAppHost.Configuration.Settings<RedisSettings>();
            var meshLockSubspace = redisSettings.MeshLockSubspace;
            if (OrdinalEquals(meshLockSubspace, "?"))
                meshLockSubspace = Alphabet.AlphaNumeric.Generator8.Next();
            var meshLockOptionsPreset =
                redisSettings.MeshLockOptionsPreset.NullIfEmpty()
                ?? nameof(MeshLockOptions.Default);
            WriteLine($"IMeshLocks settings: '{meshLockSubspace}' subspace, '{meshLockOptionsPreset}' options preset");
            Environment.SetEnvironmentVariable(MeshLockSubspaceEnvVar, meshLockSubspace);
            Environment.SetEnvironmentVariable(MeshLockOptionsPresetEnvVar, meshLockOptionsPreset);

            var (host, defaultPort) = GetDefaultHostAndPort();
            var ownUrl = $"http://{host}:{defaultPort + OwnRoleIndex}";
            WriteLine($"MultiHost mode. Own role: {OwnRole} of role group '{RoleGroupName}' @ {ownUrl}");
            for (var roleIndex = 0; roleIndex < RoleGroup.Length; roleIndex++) {
                if (roleIndex != OwnRoleIndex)
                    LaunchAppHost(RoleGroup[roleIndex], host, defaultPort + roleIndex);
            }

            Environment.SetEnvironmentVariable(UrlsEnvVar, ownUrl);
            Environment.SetEnvironmentVariable(ServerRoleEnvVar, OwnRole.Value);
            StartKeyboardWatcher();
        }
    }

    // Private methods

    private static bool TryParseRoleArgument(string[] args, string argPrefix)
    {
        var value = args
            .Select(x => x.OrdinalStartsWith(argPrefix) ? x[argPrefix.Length..].Trim().NullIfEmpty() : null)
            .SingleOrDefault(x => !x.IsNullOrEmpty());
        if (value.IsNullOrEmpty())
            return false;

        if (value.Split(RoleGroupDelimiter) is not [ var roleGroupName, var sOwnRole ])
            throw StandardError.CommandLine($"invalid argument: {argPrefix}{value} - <role-group>:<role-name> expected.");
        if (!AllRoleGroups.ContainsKey(roleGroupName))
            throw StandardError.CommandLine($"invalid argument: {argPrefix}{value} - no role group '{roleGroupName}'.");

        RoleGroupName = roleGroupName; // This changes RoleGroup
        OwnRoleIndex = Array.IndexOf(RoleGroup, sOwnRole); // This changes OwnRole
        if (OwnRoleIndex < 0)
            throw StandardError.CommandLine($"invalid argument: {argPrefix}{value} - role group '{roleGroupName}' doesn't contain '{sOwnRole}' role.");

        return true;
    }

    private static (string Host, int Port) GetDefaultHostAndPort()
    {
        var endpoint = ServerEndpoints.List(ConfigOnlyAppHost.Services, "http://").FirstOrDefault();
        return ServerEndpoints.Parse(endpoint);
    }

    private static void LaunchAppHost(HostRole role, string host, int port)
    {
        var url = $"http://{host}:{port}";
        WriteLine($"Launching host: {role} @ {url}");
        var startInfo = new ProcessStartInfo("cmd.exe") {
            ArgumentList = {
                "/C", "start", "/D", Environment.CurrentDirectory, Environment.ProcessPath!,
                $"{RoleArgPrefix}{RoleGroupName}{RoleGroupDelimiter}{role.Value}",
                UrlArgPrefix + url,
                UseKeyboard ? KeyboardArg : "",
                ForceDistributed ? ForceDistributedArg : "",
            },
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(startInfo);
    }

    private static void StartKeyboardWatcher() {
        if (!UseKeyboard)
            return;

        var (host, defaultPort) = GetDefaultHostAndPort();
        var port = defaultPort + RoleGroup.Length;
        _ = Task.Run(async () => {
            while (true) {
                if (!KeyAvailable) {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                var key = ReadKey(true).KeyChar;
                if (key is < '0' or > '9')
                    continue;

                var role = RoleGroup.GetValueOrDefault(key - '0');
                if (role.IsNone)
                    continue;

                LaunchAppHost(role, host, port++);
            }
            // ReSharper disable once FunctionNeverReturns
        }, CancellationToken.None);
    }
}
