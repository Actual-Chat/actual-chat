using System.Diagnostics.CodeAnalysis;
using ActualChat.Configuration;
using ActualChat.Hosting;
using ActualChat.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Fusion.EntityFramework.Redis;
using Stl.Fusion.Operations.Internal;

namespace ActualChat.Db.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class DbModule : HostModule<DbSettings>
{
    private const int CommandTimeout = 3;

    public DbModule(IServiceProvider moduleServices) : base(moduleServices) { }

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
            services.AddHealthChecks();
        /*
            .AddNpgSql(
                connectionStringSuffix,
                name: $"db_{contextName}",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { HealthTags.Ready });
        */

        services.AddSingleton(dbInfo);
        services.AddPooledDbContextFactory<TDbContext>(db => {
            switch (dbKind) {
            case DbKind.InMemory:
                Log.LogWarning("In-memory DB is used for {DbContext}", typeof(TDbContext).GetName());
                db.UseInMemoryDatabase(dbInfo.ConnectionString);
                db.ConfigureWarnings(warnings => { warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning); });
                break;
            case DbKind.PostgreSql:
                db.UseNpgsql(dbInfo.ConnectionString, npgsql => {
                    npgsql.CommandTimeout(CommandTimeout);
                    npgsql.EnableRetryOnFailure(0);
                    npgsql.MaxBatchSize(16); // NOTE(AY): Was 1 - not sure why, prob. related to old concurrency issues
                    npgsql.MigrationsAssembly(typeof(TDbContext).Assembly.GetName().Name + ".Migration");
                });
                db.UseNpgsqlHintFormatter();
                // To be enabled later (requires migrations):
                // builder.UseValidationCheckConstraints(c => c.UseRegex(false));
                break;
            default:
                throw StandardError.NotSupported("Unsupported database kind.");
            }
            if (IsDevelopmentInstance)
                db.EnableSensitiveDataLogging();
            db.AddInterceptors(new DbConnectionConfigurator(dbKind));
        }, 32);

        services.AddDbContextServices<TDbContext>(db => {
            services.AddSingleton(new CompletionProducer.Options {
                // Let's not waste log with successful completed command
                LogLevel = LogLevel.Debug,
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
                operations.ConfigureOperationLogTrimmer(_ => new DbOperationLogTrimmer<TDbContext>.Options {
                    MaxOperationAge = TimeSpan.FromMinutes(10),
                });
                // operations.AddNpgsqlOperationLogChangeTracking();
                operations.AddRedisOperationLogChangeTracking();

                // override DbOperationLog for efficient trimming
                services.RemoveAll(sd => sd.ServiceType == typeof(IDbOperationLog<TDbContext>));
                services.TryAddSingleton<IDbOperationLog<TDbContext>, DbOperationLog<TDbContext>>();
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
