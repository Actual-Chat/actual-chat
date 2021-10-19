﻿using System.Data;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Npgsql;
using Stl.Fusion.Operations.Internal;
using Stl.Plugins;

namespace ActualChat.Db.Module;

public class DbModule : HostModule<DbSettings>
{
    public DbModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public DbModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        var fusion = services.AddFusion();
        fusion.AddOperationReprocessor();
    }

    public void AddDefaultDbServices<TDbContext>(
        IServiceCollection services,
        string? connectionString)
        where TDbContext: DbContext
    {
        if (connectionString.IsNullOrEmpty())
            connectionString = Settings.DefaultDb;
        if (!Settings.OverrideDb.IsNullOrEmpty())
            connectionString = Settings.OverrideDb;

        // Replacing variables
        var dbNameVar = typeof(TDbContext).Name.TrimSuffix("DbContext").ToLowerInvariant();
        connectionString = connectionString.Replace("{dbName}", dbNameVar);

        // Creating DbInfo<TDbContext>
        var dbKind = connectionString.StartsWith("memory://") ? DbKind.InMemory : DbKind.Default;
        var dbInfo = new DbInfo<TDbContext>() {
            DbKind = dbKind,
            ConnectionString = connectionString,
        };

        // Adding services
        services.AddSingleton(dbInfo);
        services.AddDbContextFactory<TDbContext>(builder => {
            switch (dbKind) {
            case DbKind.Default:
                builder.UseNpgsql(connectionString);
                break;
            case DbKind.InMemory:
                Log.LogWarning("In-memory DB is used for {DbContext}", typeof(TDbContext).Name);
                builder.UseInMemoryDatabase(connectionString);
                builder.ConfigureWarnings(warnings => {
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning);
                });
                break;
            default:
                throw new ArgumentOutOfRangeException();
            }
            if (IsDevelopmentInstance)
                builder.EnableSensitiveDataLogging();
        });
        services.AddDbContextServices<TDbContext>(dbContext => {
            services.AddSingleton(new CompletionProducer.Options {
                IsLoggingEnabled = true,
            });
            services.AddTransient(c => new DbOperationScope<TDbContext>(c) {
                IsolationLevel = IsolationLevel.RepeatableRead,
            });
            dbContext.AddOperations((_, o) => {
                o.UnconditionalWakeUpPeriod = TimeSpan.FromSeconds(IsDevelopmentInstance ? 60 : 5);
            });
            if (dbKind == DbKind.Default)
                dbContext.AddNpgsqlOperationLogChangeTracking();
        });
    }
}
