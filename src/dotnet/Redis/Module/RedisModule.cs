using ActualChat.Configuration;
using ActualChat.Module;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis.Module;

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
            ("context", typeof(TContext).Name.TrimSuffix("DbContext").ToLower()));

        var parts = connectionString.Split('|', 2);
        var configuration = parts.FirstOrDefault() ?? "";
        var keyPrefix = parts.Skip(1).SingleOrDefault() ?? "";
        Log.LogInformation("RedisDb<{Context}>: configuration = '{Configuration}', keyPrefix = '{KeyPrefix}'",
            typeof(TContext).GetName(), configuration, keyPrefix);

        // RedisDb<TContext>
        var cfg = ConfigurationOptions.Parse(configuration);
        cfg.SocketManager = SocketManager.Shared;
        services.AddRedisDb<TContext>(cfg, keyPrefix);
    }

    protected override void InjectServices(IServiceCollection services)
    { }
}
