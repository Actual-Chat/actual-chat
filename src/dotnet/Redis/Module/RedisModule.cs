using ActualChat.Configuration;
using ActualChat.Hosting;
using ActualChat.Module;
using StackExchange.Redis;
using Stl.Plugins;
using Stl.Redis;

namespace ActualChat.Redis.Module;

public class RedisModule : HostModule<RedisSettings>
{
    public RedisModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public RedisModule(IPluginHost plugins) : base(plugins) { }

    public void AddRedisDb<TContext>(
        IServiceCollection services,
        string? connectionString)
    {
        if (connectionString.IsNullOrEmpty())
            connectionString = Settings.DefaultRedis;
        if (!Settings.OverrideRedis.IsNullOrEmpty())
            connectionString = Settings.OverrideRedis;

        // Replacing variables
        var instance = Plugins.GetPlugins<CoreModule>().Single().Settings.Instance;
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
            typeof(TContext).Name, configuration, keyPrefix);

        var cfg = ConfigurationOptions.Parse(configuration);
        cfg.SocketManager = SocketManager.ThreadPool;
        services.AddRedisDb<TContext>(cfg, keyPrefix);
    }
}

