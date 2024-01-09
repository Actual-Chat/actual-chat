using System.Diagnostics.CodeAnalysis;
using ActualLab.Versioning;

namespace ActualChat;

public static class HasVersionExt
{
    public static bool IsStored<TEntity>([NotNullWhen(true)] this TEntity? entity)
        where TEntity : IHasVersion<long>
        => entity is { Version: > 0 };

    public static TEntity RequireSomeVersion<TEntity>([NotNull] this TEntity? entity)
        where TEntity : IHasVersion<long>
    {
        if (entity == null)
            throw StandardError.NotFound<TEntity>();
        if (entity.Version <= 0)
            throw StandardError.Constraint("Version must be positive here.");

        return entity;
    }

    public static TEntity RequireVersion<TEntity>([NotNull] this TEntity? entity, long? expectedVersion)
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
