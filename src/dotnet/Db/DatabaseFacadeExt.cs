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
}
