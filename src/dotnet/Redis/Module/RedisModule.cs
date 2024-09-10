using System.Diagnostics.CodeAnalysis;
using ActualChat.Configuration;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.Module;
using StackExchange.Redis;
using ActualLab.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Redis.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class RedisModule(IServiceProvider moduleServices)
    : HostModule<RedisSettings>(moduleServices), IServerModule
{
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

        var cfg = ConfigurationOptions.Parse(configuration);
        cfg.SocketManager = SocketManager.ThreadPool;
        services.AddRedisDb<TContext>(cfg, keyPrefix);
        services.AddSingleton<IMeshLocks<TContext>>(
            c => new RedisMeshLocks<TContext>(c) {
                LockOptions = c.GetRequiredService<MeshLockOptions>(),
            });
    }

    protected override void InjectServices(IServiceCollection services)
        => services.TryAddSingleton(c => {
            var lockOptions = MeshLockOptions.Default;
            if (c.HostInfo().IsTested)
                lockOptions = lockOptions with {
                    ExpirationPeriod = TimeSpan.FromSeconds(10),
                    UnconditionalCheckPeriod = TimeSpan.FromSeconds(3),
                };
            return lockOptions;
        });
}
