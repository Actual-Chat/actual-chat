using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ActualLab.Fusion.EntityFramework.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace ActualChat.EntityFramework;

public class DbEntityResolver<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbContext,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbEntity>
    : ActualLab.Fusion.EntityFramework.DbEntityResolver<TDbContext, TKey, TDbEntity>
    where TDbContext : DbContext
    where TKey : notnull
    where TDbEntity : class
{
    private static MethodInfo EnumerableContainsMethod { get; }
        = new Func<IEnumerable<TKey>, TKey, bool>(Enumerable.Contains).Method;

    public DbEntityResolver(Options settings, IServiceProvider services) : base(settings, services)
    {
        // Re-build compiled queries for Npgsql
        var compiledQuery = CreateCompiledQueryNpgsql();
        Queries = Enumerable.Range(1, Settings.BatchSize)
            .Select(batchSize => (compiledQuery, batchSize))
            .ToArray();
    }

    private Func<TDbContext, TKey[], IAsyncEnumerable<TDbEntity>> CreateCompiledQueryNpgsql()
    {
        var pDbContext = Expression.Parameter(typeof(TDbContext), "dbContext");
        var pKeys = Expression.Parameter(typeof(TKey[]), "pKeys");
        var pEntity = Expression.Parameter(typeof(TDbEntity), "e");

        // entity.Key expression
        var eKey = KeyExtractorExpression.Body.Replace(KeyExtractorExpression.Parameters[0], pEntity);

        // .Where predicate expression
        var ePredicate = Expression.Call(EnumerableContainsMethod, pKeys, eKey);
        var lPredicate = Expression.Lambda<Func<TDbEntity, bool>>(ePredicate!, pEntity);

        // dbContext.Set<TDbEntity>().Where(...)
        var eEntitySet = Expression.Call(pDbContext, DbContextSetMethod);
        var eWhere = Expression.Call(null, QueryableWhereMethod, eEntitySet, Expression.Quote(lPredicate));

        // Applying QueryTransformer
        var qt = Settings.QueryTransformer;
        var eBody = qt == null
            ? eWhere
            : qt.Body.Replace(qt.Parameters[0], eWhere);

        // Creating compiled query
        var lambda = Expression.Lambda(eBody, [pDbContext, pKeys]);
#pragma warning disable EF1001
        var query = new CompiledAsyncEnumerableQuery<TDbContext, TDbEntity>(lambda);
#pragma warning restore EF1001

        // Locating query.Execute methods
        var mExecute = query.GetType()
            .GetMethods()
            .SingleOrDefault(m => Equals(m.Name, nameof(query.Execute))
                && m.IsGenericMethod
                && m.GetGenericArguments().Length == 1)
#pragma warning disable IL2060
            ?.MakeGenericMethod(typeof(TKey[]));
#pragma warning restore IL2060
        if (mExecute == null)
            throw Errors.BatchSizeIsTooLarge();

        // Creating compiled query invoker
        var eExecuteCall = Expression.Call(Expression.Constant(query), mExecute, pDbContext, pKeys);
        return (Func<TDbContext, TKey[], IAsyncEnumerable<TDbEntity>>)Expression.Lambda(eExecuteCall, pDbContext, pKeys).Compile();
    }

}
