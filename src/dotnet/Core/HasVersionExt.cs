using System.Diagnostics.CodeAnalysis;
using Stl.Versioning;

namespace ActualChat;

public static class HasVersionExt
{
    public static bool IsStored<TEntity>([NotNullWhen(true)] this TEntity? entity)
        where TEntity : IHasVersion<long>
        => entity != null && entity.Version != 0;

    public static TEntity RequireVersion<TEntity>(this TEntity? entity)
        where TEntity : IHasVersion<long>
    {
        if (entity == null)
            throw StandardError.NotFound<TEntity>();
        if (entity.Version == default)
            throw StandardError.Constraint("Version cannot be default here.");

        return entity;
    }

    public static TEntity RequireVersion<TEntity>(this TEntity? entity, long? expectedVersion)
        where TEntity : IHasVersion<long>
    {
        if (entity == null)
            throw StandardError.NotFound<TEntity>();

        VersionChecker.RequireExpected(entity.Version, expectedVersion);
        return entity;
    }

    public static async Task<TEntity> RequireVersion<TEntity>(this Task<TEntity?> entityTask, long? expectedVersion)
        where TEntity : IHasVersion<long>
    {
        var entity = await entityTask.ConfigureAwait(false);
        if (entity == null)
            throw StandardError.NotFound<TEntity>();

        VersionChecker.RequireExpected(entity.Version, expectedVersion);
        return entity;
    }

    public static async ValueTask<TEntity> RequireVersion<TEntity>(this ValueTask<TEntity?> entityTask, long? expectedVersion)
        where TEntity : IHasVersion<long>
    {
        var entity = await entityTask.ConfigureAwait(false);
        if (entity == null)
            throw StandardError.NotFound<TEntity>();

        VersionChecker.RequireExpected(entity.Version, expectedVersion);
        return entity;
    }
}
