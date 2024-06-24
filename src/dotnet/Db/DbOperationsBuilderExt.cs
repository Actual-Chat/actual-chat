using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations.LogProcessing;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db;

public static class DbOperationsBuilderExt
{
    public static DbOperationsBuilder<TDbContext> ConfigureEventLogReader<TDbContext>(
        this DbOperationsBuilder<TDbContext> operationsBuilder,
        Func<IServiceProvider, DbEventLogReader<TDbContext>.Options> optionsFactory)
        where TDbContext : DbContext
    {
        operationsBuilder.Services.AddSingleton(optionsFactory);
        return operationsBuilder;
    }
}
