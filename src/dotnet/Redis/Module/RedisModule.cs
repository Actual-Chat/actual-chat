using ActualChat.Configuration;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.Module;
using StackExchange.Redis;
using ActualLab.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Redis.Module;

public sealed class RedisModule(IServiceProvider moduleServices)
    : HostModule<RedisSettings>(moduleServices), IServerModule
{
    private readonly Lock _lock = new();

    [field: AllowNull, MaybeNull]
    public string MeshLockSubspace {
        get {
            if (field != null)
                return field;

            using var _ = _lock.EnterScope();
            if (field != null)
                return field;

            var value = Settings.MeshLockSubspace;
            if (OrdinalEquals(value, "?"))
                value = Alphabet.AlphaNumeric.Generator8.Next();
            return field = value;
        }
    }

    public void AddRedisDb<TContext>(
        IServiceCollection services,
        string? connectionString = null)
    {
        if (connectionString.IsNullOrEmpty())
            connectionString = Settings.DefaultRedis;
        if (!Settings.OverrideRedis.IsNullOrEmpty())
            connectionString = Settings.OverrideRedis;

        // Replacing variables
        var instance = Host.GetModule<CoreModule>().Settings.Instance;
        connectionString = Variables.Inject(connectionString,
            ("instance", instance),
            ("instance_", instance.IsNullOrEmpty() ? "" : $"{instance}_"),
            ("instance.", instance.IsNullOrEmpty() ? "" : $"{instance}."),
            ("_instance", instance.IsNullOrEmpty() ? "" : $"_{instance}"),
            (".instance", instance.IsNullOrEmpty() ? "" : $".{instance}"),
            ("context", typeof(TContext).Name.TrimSuffix("DbContext").ToLowerInvariant()));

        var parts = connectionString.Split('|', 2);
        var configuration = parts.FirstOrDefault() ?? "";
        var keyPrefix = parts.Skip(1).SingleOrDefault() ?? "";
        Log.LogInformation("RedisDb<{Context}>: configuration = '{Configuration}', keyPrefix = '{KeyPrefix}'",
            typeof(TContext).GetName(), configuration, keyPrefix);

        // RedisDb<TContext>
        var cfg = ConfigurationOptions.Parse(configuration);
        cfg.SocketManager = SocketManager.ThreadPool;
        services.AddRedisDb<TContext>(cfg, keyPrefix);

        // IMeshLocks<TContext>
        services.AddSingleton<IMeshLocks<TContext>>(c => {
            var subspace = MeshLockSubspace;
            var optionsPreset = Settings.MeshLockOptionsPreset.NullIfEmpty()
                ?? nameof(MeshLockOptions.Default);
            Log.LogInformation("IMeshLocks<{Context}>: '{Subspace}' subspace, '{OptionsPreset}' lock options preset",
                typeof(TContext).GetName(), subspace, optionsPreset);

            // ReSharper disable once VariableHidesOuterVariable
            var keyPrefix = subspace.IsNullOrEmpty()
                ? RedisMeshLocks.DefaultKeyPrefix
                : $"{RedisMeshLocks.DefaultKeyPrefix}-{subspace}"; // Must not use "." as a delimiter!
            return new RedisMeshLocks<TContext>(c, keyPrefix) {
                LockOptions = MeshLockOptions.Presets[optionsPreset],
            };
        });
    }

    protected override void InjectServices(IServiceCollection services)
    { }
}
