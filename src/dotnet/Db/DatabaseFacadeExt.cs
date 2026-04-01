using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ActualChat.Db;

public static class DatabaseFacadeExt
{
    public static TService GetRelationalService<TService>(this IInfrastructure<IServiceProvider> db)
    {
        var service = db.Instance.GetService<TService>();
        return service ?? throw new InvalidOperationException(RelationalStrings.RelationalNotInUse);
    }

    public static ClosedDisposable<(DatabaseFacade Database, int? OriginalTimeout)> UseCommandTimeout(
        this DatabaseFacade database, int timeoutInSeconds)
    {
        var originalTimeout = database.GetCommandTimeout();
        database.SetCommandTimeout(timeoutInSeconds);
        return Disposable.NewClosed(
            (Database: database, OriginalTimeout: originalTimeout),
            state => state.Database.SetCommandTimeout(state.OriginalTimeout));
    }
}
