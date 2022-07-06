using System.Data.Common;
using System.Text;
using ActualChat.Configuration;
using ActualChat.Db.MySql;
using ActualChat.Hosting;
using ActualChat.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        string? connectionString)
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
        services.TryAddSingleton<OutstandingDbConnectionsCounter>();
        services.AddSingleton(dbInfo);
        services.AddDbContextFactory<TDbContext>((svp, builder) => {
            var counter = svp.GetRequiredService<OutstandingDbConnectionsCounter>();
            var logger = svp.GetRequiredService<ILogger<DbModule>>();
            // builder.LogTo(
            //     data => logger.LogInformation(data),
            //     new[] {
            //         // CoreEventId.ContextInitialized,
            //         // CoreEventId.ContextInitialized,
            //         RelationalEventId.ConnectionOpened,
            //         RelationalEventId.ConnectionClosed
            //     });
            builder.LogTo(
                (eventId, level) =>
                        eventId.Id == CoreEventId.ContextInitialized ||
                        eventId.Id == CoreEventId.ContextDisposed ||
                        eventId.Id == RelationalEventId.ConnectionOpened ||
                        eventId.Id == RelationalEventId.ConnectionClosed ||
                        eventId.Id == RelationalEventId.ConnectionError ||
                        eventId.Id == RelationalEventId.ConnectionOpening ||
                        eventId.Id == RelationalEventId.ConnectionClosing
                ,data => {
                    string message = string.Empty;
                    string extraMessage = string.Empty;
                    if (data is ConnectionEndEventData endEventData) {
                        var connectionId = endEventData.ConnectionId;
                        var dbName = endEventData.Connection.Database;
                        if (endEventData.EventId == RelationalEventId.ConnectionOpened.Id) {
                            var count = counter.Increment(dbName);
                            message = $"Connection Opened ({connectionId}). Db: '{dbName}' ({count})";
                            if (counter.CheckDump()) {
                                var dbConnection = endEventData.Connection;
                                extraMessage += DumpConnections(dbConnection);
                            }
                        }
                        else if (endEventData.EventId == RelationalEventId.ConnectionClosed.Id) {
                            var count = counter.Decrement(dbName);
                            message = $"Connection Closed ({connectionId}). Db: '{dbName}' ({count})";
                        }
                        else if (endEventData.EventId == RelationalEventId.ConnectionError.Id) {
                            var errorEventData = (ConnectionErrorEventData)endEventData;
                            message = $"Connection Error ({connectionId}). Db: '{dbName}'. Error details: {errorEventData.Exception}";
                        }
                        else {

                        }
                    }
                    else {

                    }
                    logger.LogInformation(!message.IsNullOrEmpty() ? message : data.ToString());
                    if (!extraMessage.IsNullOrEmpty())
                        logger.LogInformation(extraMessage);
                });
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
        services.AddDbContextServices<TDbContext>(dbContext => {
            services.AddSingleton(new CompletionProducer.Options {
                LogLevel = LogLevel.Information,
            });
            /*
            services.AddTransient(c => new DbOperationScope<TDbContext>(c) {
                IsolationLevel = IsolationLevel.RepeatableRead,
            });
            */
            dbContext.AddOperations(_ => new() {
                UnconditionalCheckPeriod = TimeSpan.FromSeconds(IsDevelopmentInstance ? 60 : 5).ToRandom(0.1),
            });
            dbContext.AddRedisOperationLogChangeTracking();
        });
    }

    private static string DumpConnections(DbConnection dbConnection)
    {
        var sb = new StringBuilder();
        using (var cmd = dbConnection.CreateCommand()) {
            cmd.CommandText = "SELECT * FROM pg_stat_activity";
            using (var reader = cmd.ExecuteReader()) {
                if (reader.HasRows) {
                    var line = string.Empty;
                    var queryIndex = -1;
                    for (int i = 0; i < reader.FieldCount; i++) {
                        var columnName = reader.GetName(i);
                        if (columnName == "query")
                            queryIndex = i;
                        if (!line.IsNullOrEmpty())
                            line += "\t";
                        line += columnName;
                    }
                    sb.AppendLine(line);

                    var rowsNumber = 0;
                    while (reader.Read()) {
                        line = String.Empty;
                        for (int i = 0; i < reader.FieldCount; i++) {
                            var cellValue = reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString();
                            if (queryIndex >= 0 && queryIndex == i)
                                cellValue = LinerizeQuery(cellValue);
                            if (!line.IsNullOrEmpty())
                                line += "\t";
                            line += cellValue;
                        }
                        sb.AppendLine(line);
                        rowsNumber++;
                    }
                    sb.AppendLine("Total rows: " + rowsNumber);
                }
            }
        }
        return sb.ToString();
    }

    private static string LinerizeQuery(string query)
        => query
            .Replace(Environment.NewLine, " ")
            .Replace("\n", " ");

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        var fusion = services.AddFusion();
        fusion.AddOperationReprocessor();
    }
}
