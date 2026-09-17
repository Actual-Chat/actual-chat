using System.Collections.Concurrent;
using ActualChat.Db;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Media;

public class ImageSuggestionsBackend(IServiceProvider services)
    : DbServiceBase<MediaDbContext>(services), IImageSuggestionsBackend
{
    // Every command for one key shards to one node, so TryAdd here is an atomic
    // "is a generation already running" test for the keys this node owns - no distributed lock
    // needed. Losing the dictionary with the node is fine: a generation is ~10s, and whoever picks
    // up the shard behaves as if none was running.
    private readonly ConcurrentDictionary<string, Moment> _generations = new();

    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();

    private IDbEntityResolver<string, DbImageSuggestion> DbSuggestionResolver
        => field ??= Services.DbEntityResolver<string, DbImageSuggestion>();
    private IImageGenerations ImageGenerations => field ??= Services.GetRequiredService<IImageGenerations>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();

    // [ComputeMethod]
    public virtual async Task<ImageSuggestion?> Get(string key, CancellationToken cancellationToken)
    {
        if (key.IsNullOrEmpty())
            return null;

        var dbSuggestion = await DbSuggestionResolver.Get(key, cancellationToken).ConfigureAwait(false);
        if (dbSuggestion?.ToModel() is not { } suggestion)
            return null;

        var media = await MediaBackend.Get(suggestion.MediaId, cancellationToken).ConfigureAwait(false);
        return suggestion with { Media = media };
    }

    // [ComputeMethod]
    public virtual async Task<Moment?> GetDismissedUntil(string key, CancellationToken cancellationToken)
    {
        if (key.IsNullOrEmpty())
            return null;

        var dbSuggestion = await DbSuggestionResolver.Get(key, cancellationToken).ConfigureAwait(false);
        return dbSuggestion?.DismissedUntil is { } dismissedUntil ? new Moment(dismissedUntil) : null;
    }

    // [ComputeMethod]
    public virtual Task<Moment?> GetGenerationStartedAt(string key, CancellationToken cancellationToken)
        => Task.FromResult(_generations.TryGetValue(key, out var startedAt) ? startedAt : (Moment?)null);

    // Deliberately not a [ComputeMethod] - see IImageSuggestionsBackend
    public async Task<ApiArray<string>> ListStale(
        Moment maxCreatedAt,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var maxCreatedAtValue = maxCreatedAt.ToDateTime();
        var now = Clocks.SystemClock.Now.ToDateTime();
        var keys = await dbContext.ImageSuggestions
            .Where(x => x.CreatedAt < maxCreatedAtValue)
            // A dismissal still in force keeps its row: re-offering the moment it expires is the point
            .Where(x => x.DismissedUntil == null || x.DismissedUntil < now)
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return keys.ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<ImageSuggestion?> OnGenerate(
        ImageSuggestionsBackend_Generate command,
        CancellationToken cancellationToken)
    {
        var key = command.Key;
        if (Invalidation.IsActive) {
            _ = Get(key, default);
            _ = GetDismissedUntil(key, default);
            return default!;
        }

        if (!ImageGenerations.IsAvailable)
            return null;

        if (!_generations.TryAdd(key, Clocks.SystemClock.Now))
            return await Get(key, cancellationToken).ConfigureAwait(false);

        InvalidateGenerationStartedAt(key);
        try {
            // Re-read now that we hold the slot: a generation may have finished just before us.
            // An explicit regenerate wants a new image regardless of what is already there.
            var dbSuggestion = await DbSuggestionResolver.Get(key, cancellationToken).ConfigureAwait(false);
            if (!command.IsExplicit && dbSuggestion?.ToModel() is not null)
                return await Get(key, cancellationToken).ConfigureAwait(false);

            var replacedMediaId = dbSuggestion?.ToModel()?.MediaId;

            var spec = new ImageGenerationSpec(key, command.ImageDescription, command.Style) {
                MediaKind = command.MediaKind,
                Width = command.Width,
                Height = command.Height,
                Seed = command.Seed,
            };
            var mediaId = await ImageGenerations.Generate(spec, cancellationToken).ConfigureAwait(false);
            if (mediaId is null)
                return null;

            var stored = await Store(key, mediaId, command.ImageDescription, cancellationToken).ConfigureAwait(false);
            // After the row is committed, so a failed commit can never strand the chat without one
            await DeleteMedia(replacedMediaId, cancellationToken).ConfigureAwait(false);
            return stored;
        }
        finally {
            _generations.TryRemove(key, out _);
            InvalidateGenerationStartedAt(key);
        }
    }

    // [CommandHandler]
    public virtual async Task OnDismiss(ImageSuggestionsBackend_Dismiss command, CancellationToken cancellationToken)
    {
        var key = command.Key;
        if (Invalidation.IsActive) {
            _ = GetDismissedUntil(key, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSuggestion = await dbContext.ImageSuggestions.Get(key, cancellationToken).ConfigureAwait(false);
        if (dbSuggestion is null) {
            dbSuggestion = new DbImageSuggestion {
                Id = key,
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbSuggestion);
        }
        dbSuggestion.DismissedUntil = command.DismissedUntil;
        dbSuggestion.Version = VersionGenerator.NextVersion(dbSuggestion.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemove(ImageSuggestionsBackend_Remove command, CancellationToken cancellationToken)
    {
        var key = command.Key;
        if (Invalidation.IsActive) {
            _ = Get(key, default);
            _ = GetDismissedUntil(key, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSuggestion = await dbContext.ImageSuggestions.Get(key, cancellationToken).ConfigureAwait(false);
        if (dbSuggestion is null)
            return;

        var mediaId = dbSuggestion.ToModel()?.MediaId;
        dbContext.Remove(dbSuggestion);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // An accepted suggestion keeps its media - it is the content's own picture now
        if (command.MustDeleteMedia)
            await DeleteMedia(mediaId, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<ImageSuggestion?> Store(
        string key,
        MediaId mediaId,
        string imageDescription,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSuggestion = await dbContext.ImageSuggestions.Get(key, cancellationToken).ConfigureAwait(false);
        if (dbSuggestion is null) {
            dbSuggestion = new DbImageSuggestion { Id = key };
            dbContext.Add(dbSuggestion);
        }
        dbSuggestion.MediaId = mediaId.Value;
        dbSuggestion.ImageDescription = imageDescription;
        dbSuggestion.CreatedAt = Clocks.SystemClock.Now;
        dbSuggestion.DismissedUntil = null; // A fresh suggestion is a new offer
        dbSuggestion.Version = VersionGenerator.NextVersion(dbSuggestion.Version);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSuggestion.ToModel();
    }

    // Best-effort: a suggestion's media is never referenced anywhere else, and failing to collect
    // one leaks ~50KB that the sweep picks up later. Losing the row over it would be worse.
    private async Task DeleteMedia(MediaId? mediaId, CancellationToken cancellationToken)
    {
        if (mediaId is null)
            return;

        try {
            var change = new MediaBackend_Change(mediaId, null, Change.Remove<MediaFull>());
            await Commander.Call(change, true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to delete media '{MediaId}' of a replaced suggestion", mediaId);
        }
    }

    private void InvalidateGenerationStartedAt(string key)
    {
        using (Invalidation.Begin())
            _ = GetGenerationStartedAt(key, default);
    }
}
