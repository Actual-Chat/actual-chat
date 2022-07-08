using ActualChat.Configuration;
using ActualChat.Db.MySql;
using ActualChat.Hosting;
using ActualChat.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Redis;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Db.Module;

public class DbModule : HostModule<DbSettings>
{
    public DbModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public DbModule(IPluginHost plugins) : base(plugins) { }

    public void AddDbContextServices<TDbContext>(
        IServiceCollection services,
        string? connectionString,
        Action<DbContextBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext
    {
        if (connectionString.IsNullOrEmpty())
            connectionString = Settings.DefaultDb;
        if (!Settings.OverrideDb.IsNullOrEmpty())
            connectionString = Settings.OverrideDb;

        // Replacing variables
        var instance = Plugins.GetPlugins<CoreModule>().Single().Settings.Instance;
        connectionString = Variables.Inject(connectionString,
            ("instance", instance),
            ("instance_", instance.IsNullOrEmpty() ? "" : $"{instance}_"),
            ("instance.", instance.IsNullOrEmpty() ? "" : $"{instance}."),
            ("_instance", instance.IsNullOrEmpty() ? "" : $"_{instance}"),
            (".instance", instance.IsNullOrEmpty() ? "" : $".{instance}"),
            ("context", typeof(TDbContext).Name.TrimSuffix("DbContext").ToLowerInvariant()));

        // Creating DbInfo<TDbContext>
        var (dbKind, connectionStringSuffix) = connectionString switch {
            { } s when s.OrdinalHasPrefix("memory:", out var suffix)
                => (DbKind.InMemory, suffix.Trim()),
            { } s when s.OrdinalHasPrefix("postgresql:", out var suffix)
                => (DbKind.PostgreSql, suffix.Trim()),
            { } s when s.OrdinalHasPrefix("mysql:", out var suffix)
                => (DbKind.MySql, suffix.Trim()),
            _ => throw new InvalidOperationException("Unrecognized database connection string"),
        };
        var dbInfo = new DbInfo<TDbContext> {
            DbKind = dbKind,
            ConnectionString = connectionStringSuffix,
            ShouldRecreateDb = Settings.ShouldRecreateDb,
            ShouldVerifyDb = Settings.ShouldVerifyDb,
            ShouldMigrateDb = Settings.ShouldMigrateDb,
        };

        // Adding services
        services.AddSingleton(dbInfo);
        services.AddDbContextFactory<TDbContext>(builder => {
            switch (dbKind) {
            case DbKind.InMemory:
                Log.LogWarning("In-memory DB is used for {DbContext}", typeof(TDbContext).Name);
                builder.UseInMemoryDatabase(dbInfo.ConnectionString);
                builder.ConfigureWarnings(warnings => { warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning); });
                break;
            case DbKind.PostgreSql:
                builder.UseNpgsql(dbInfo.ConnectionString, npgsql => {
                    npgsql.EnableRetryOnFailure(0);
                    npgsql.MaxBatchSize(1);
                    npgsql.MigrationsAssembly(typeof(TDbContext).Assembly.GetName().Name + ".Migration");
                });
                builder.UseNpgsqlHintFormatter();
                // To be enabled later (requires migrations):
                // builder.UseValidationCheckConstraints(c => c.UseRegex(false));
                break;
            case DbKind.MySql:
                var serverVersion = ServerVersion.AutoDetect(dbInfo.ConnectionString);
                builder.UseMySql(dbInfo.ConnectionString, serverVersion, mySql => {
                    mySql.EnableRetryOnFailure(0);
                    // mySql.MaxBatchSize(1);
                    mySql.MigrationsAssembly(typeof(TDbContext).Assembly.GetName().Name + ".Migration");
                });
                builder.UseMySqlHintFormatter();
                // To be enabled later (requires migrations):
                // builder.UseValidationCheckConstraints(c => c.UseRegex(false));
                break;
            default:
                throw new NotSupportedException();
            }
            if (IsDevelopmentInstance)
                builder.EnableSensitiveDataLogging();
        });
        services.AddDbContextServices<TDbContext>(db => {
            services.AddSingleton(new CompletionProducer.Options {
                LogLevel = LogLevel.Information,
            });
            /*
            services.AddTransient(c => new DbOperationScope<TDbContext>(c) {
                IsolationLevel = IsolationLevel.RepeatableRead,
            });
            */
            db.AddOperations(operations => {
                operations.ConfigureOperationLogReader(_ => new() {
                    UnconditionalCheckPeriod = TimeSpan.FromSeconds(IsDevelopmentInstance ? 60 : 5).ToRandom(0.1),
                });
                operations.AddRedisOperationLogChangeTracking();
            });

            configure?.Invoke(db);
        });
    }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        var fusion = services.AddFusion();
        fusion.AddOperationReprocessor();
    }
}
