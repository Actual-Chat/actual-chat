using ActualChat.Resilience;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Media;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class MediaService(IServiceProvider services) : IMedia
{
    private IServiceProvider Services { get; } = services;
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaProgressBackend MediaProgressBackend { get; } = services.GetRequiredService<IMediaProgressBackend>();
    private IUploadsBackend UploadsBackend { get; } = services.GetRequiredService<IUploadsBackend>();
    private ICommander Commander { get; } = services.Commander();
    private IImageGenerations ImageGenerations => field ??= Services.GetRequiredService<IImageGenerations>();
    private RateLimitPolicy RateLimitPolicy => field ??= Services.GetRequiredService<RateLimitPolicy>();

    private RateLimitIdentityResolver IdentityResolver
        => field ??= Services.GetRequiredService<RateLimitIdentityResolver>();

    // [ComputeMethod]
    public virtual async Task<MediaProgress?> GetProgress(
        Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        if (!media.BlobId.IsNullOrEmpty())
            return new MediaProgress(mediaId, 0, MediaProcessingStage.Ready, 100);

        var progress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return progress;
    }

    // [ComputeMethod]
    public virtual async Task<MediaRef?> GetContent(
        Session session, MediaId mediaId, CancellationToken cancellationToken)
    {
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return null;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        var blobId = media.BlobId;
        if (blobId.IsNullOrEmpty())
            return null;

        var thumbnailId = media.ThumbnailId;
        var thumbnailMedia = thumbnailId != null
            ? await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false)
            : null;

        var thumbnailBlobId = thumbnailMedia?.BlobId;
        return new MediaRef(mediaId, blobId, thumbnailId, thumbnailBlobId);
    }

    // [CommandHandler]
    public virtual async Task<MediaId> OnReserveMedia(Media_ReserveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var session = command.Session;
        var scope = command.Scope;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);

        var mediaId = MediaId.New(scope);
        var media = new MediaFull(mediaId) {
            UserId = account.Id,
            Kind = command.Kind,
            Metadata = command.Metadata,
            Placeholder = command.Placeholder,
        };
        var mediaChange = new Change<MediaFull> { Create = media };

        await Commander.Call(new MediaBackend_Change(mediaId, null, mediaChange), cancellationToken)
            .ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, MediaProcessingStage.Reserved, 0);
        var progressChange = new Change<MediaProgress> { Create = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, null, progressChange), cancellationToken)
            .ConfigureAwait(false);

        return mediaId;
    }

    // [CommandHandler]
    public virtual async Task<MediaRef?> OnGenerate(Media_Generate command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        // Scope is not checked beyond requiring an account, exactly as OnReserveMedia does not check
        // it: generating into a scope is an upload you did not have to take yourself. What the check
        // below guards is money, not access.
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        await CheckGenerationRateLimit(nameof(OnGenerate), cancellationToken).ConfigureAwait(false);

        var spec = new ImageGenerationSpec(command.Scope, command.Description, command.Style) {
            // The caller is expected to remove it once it has served its purpose, which is owner-only
            OwnerId = account.Id,
            MediaKind = command.Kind,
            IsBackground = command.IsBackground,
            // Without one the provider picks its own, so two generations of one description differ
            Seed = Random.Shared.NextInt64(1, int.MaxValue),
        };
        var mediaId = await ImageGenerations.Generate(spec, cancellationToken).ConfigureAwait(false);
        if (mediaId is null)
            return null;

        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        return media?.ToMediaRef();
    }

    // [CommandHandler]
    public virtual async Task OnRemoveMedia(Media_RemoveMedia command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var mediaId = command.MediaId;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            return;

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var mediaChange = new Change<MediaFull> { Remove = true };
        await Commander.Call(new MediaBackend_Change(mediaId, null, mediaChange), cancellationToken)
            .ConfigureAwait(false);

        var progressChange = new Change<MediaProgress> { Remove = true };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, null, progressChange), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpdateProgress(Media_UpdateProgress command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var session = command.Session;
        var mediaId = command.MediaId;
        var expectedVersion = command.ExpectedVersion;
        var stage = command.Stage;
        var stageProgress = command.StageProgress;
        var error = command.Error;
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);

        var progress = new MediaProgress(mediaId, 0, stage, stageProgress, error);
        var change = new Change<MediaProgress> { Update = progress };
        await Commander.Call(new MediaProgressBackend_Change(mediaId, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<MediaRef> OnProcessUpload(
        Media_ProcessUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var session = command.Session;
        var mediaId = command.MediaId;
        var uploadId = command.UploadId;

        // Verify ownership
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();

        await RequireOwner(session, media, cancellationToken).ConfigureAwait(false);
        var upload = await UploadsBackend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null || upload.UserId != media.UserId)
            throw StandardError.Upload.NotFound();

        // Process upload and bind to media
        var mediaRef = await Commander
            .Call(new UploadsBackend_ProcessAndSaveContent(uploadId, mediaId), cancellationToken)
            .ConfigureAwait(false);

        // Remove the upload
        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken)
            .ConfigureAwait(false);

        return mediaRef;
    }

    // Private methods

    private async Task CheckGenerationRateLimit(string method, CancellationToken cancellationToken)
    {
        var source = RateLimitSource.ForConnection(RpcInboundContext.Current?.Peer.ConnectionState.Value.Connection);
        var identities = new RateLimitIdentity[RateLimitIdentityResolver.MaxIdentityCount];
        var identityCount = await IdentityResolver
            .Resolve(RateLimitPolicy, RateLimitClass.ImageGeneration, source, identities, cancellationToken)
            .ConfigureAwait(false);
        await RateLimitPolicy
            .Check(method, RateLimitClass.ImageGeneration, identities.AsSpan(0, identityCount), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RequireOwner(Session session, MediaFull media, CancellationToken cancellationToken)
    {
        if (media.UserId == null)
            throw StandardError.Unauthorized("You don't have permission to access this media.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (media.UserId != account.Id)
            throw StandardError.Unauthorized("You don't have permission to access this media.");
    }
}
