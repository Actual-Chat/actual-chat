using System.Diagnostics.CodeAnalysis;
using ActualChat.Configuration;
using ActualChat.Hosting;
using ActualChat.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Redis;
using Stl.Fusion.Operations.Internal;

namespace ActualChat.Db.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class DbModule : HostModule<DbSettings>
{
    private const int CommandTimeout = 3;

    private ILogger Log { get; }

    public DbModule(IServiceProvider services) : base(services)
        => Log = services.LogFor<DbModule>();

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
        var instance = Host.GetModule<CoreModule>().Settings.Instance;
        var contextName = typeof(TDbContext).Name.TrimSuffix("DbContext").ToLowerInvariant();
        connectionString = Variables.Inject(connectionString,
            ("instance", instance),
            ("instance_", instance.IsNullOrEmpty() ? "" : $"{instance}_"),
            ("instance.", instance.IsNullOrEmpty() ? "" : $"{instance}."),
            ("_instance", instance.IsNullOrEmpty() ? "" : $"_{instance}"),
            (".instance", instance.IsNullOrEmpty() ? "" : $".{instance}"),
            ("context", contextName));

        // Creating DbInfo<TDbContext>
        var (dbKind, connectionStringSuffix) = connectionString switch {
            { } s when s.OrdinalHasPrefix("memory:", out var suffix)
                => (DbKind.InMemory, suffix.Trim()),
            { } s when s.OrdinalHasPrefix("postgresql:", out var suffix)
                => (DbKind.PostgreSql, suffix.Trim()),
            _ => throw StandardError.Format("Unrecognized database connection string."),
        };
        var dbInfo = new DbInfo<TDbContext> {
            DbKind = dbKind,
            ConnectionString = connectionStringSuffix,
            ShouldRecreateDb = Settings.ShouldRecreateDb,
            ShouldMigrateDb = Settings.ShouldMigrateDb,
            ShouldRepairDb = Settings.ShouldRepairDb,
            ShouldVerifyDb = Settings.ShouldVerifyDb,
        };

        // Adding services
        if (dbKind == DbKind.PostgreSql)
            services.AddHealthChecks().AddNpgSql(connectionStringSuffix, name: $"db_{contextName}", tags: new[] { HealthTags.Ready });

        services.AddSingleton(dbInfo);
        services.AddDbContextFactory<TDbContext>(builder => {
            switch (dbKind) {
            case DbKind.InMemory:
                Log.LogWarning("In-memory DB is used for {DbContext}", typeof(TDbContext).GetName());
                builder.UseInMemoryDatabase(dbInfo.ConnectionString);
                builder.ConfigureWarnings(warnings => { warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning); });
                break;
            case DbKind.PostgreSql:
                builder.UseNpgsql(dbInfo.ConnectionString, npgsql => {
                    npgsql.CommandTimeout(CommandTimeout);
                    npgsql.EnableRetryOnFailure(0);
                    npgsql.MaxBatchSize(1);
                    npgsql.MigrationsAssembly(typeof(TDbContext).Assembly.GetName().Name + ".Migration");
                });
                builder.UseNpgsqlHintFormatter();
                // To be enabled later (requires migrations):
                // builder.UseValidationCheckConstraints(c => c.UseRegex(false));
                break;
            default:
                throw StandardError.NotSupported("Unsupported database kind.");
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
                // operations.AddNpgsqlOperationLogChangeTracking();
                operations.AddRedisOperationLogChangeTracking();
            });

            configure?.Invoke(db);
        });
    }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        var fusion = services.AddFusion();
        fusion.AddOperationReprocessor();
    }
}
