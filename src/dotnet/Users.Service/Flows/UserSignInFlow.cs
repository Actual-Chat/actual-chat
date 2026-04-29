using ActualChat.Flows;
using ActualChat.Uploads;

namespace ActualChat.Users.Flows;

/// <summary>
/// Handles post sign-in user account housekeeping.
/// </summary>
[Flow(DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class UserSignInFlow : Flow<Unit>
{
    public const string HttpClientName = nameof(UserSignInFlow);

    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IAvatarsBackend AvatarsBackend => field ??= Services.GetRequiredService<IAvatarsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private IMediaProcessor MediaProcessor => field ??= Services.GetRequiredService<IMediaProcessor>();
    private IMediaSaver MediaSaver => field ??= Services.GetRequiredService<IMediaSaver>();
    private IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    private ICommander Commander => field ??= Services.Commander();

    private UserId UserId => field ??= UserId.Parse(Id.Arguments);

    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public bool IsAvatarUpdated { get; set; }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        if (!IsAvatarUpdated)
            IsAvatarUpdated = await TryUpdateAvatar(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryUpdateAvatar(CancellationToken cancellationToken)
    {
        var account = await AccountsBackend.Get(UserId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return false;

        var googlePictureUrl = account.Claims.GetValueOrDefault(Constants.User.Claims.GooglePicture, "").NullIfEmpty();
        if (googlePictureUrl is null)
            return false;
        if (!Uri.TryCreate(googlePictureUrl, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;

        var avatar = await GetDefaultAvatar(account, cancellationToken).ConfigureAwait(false);
        if (avatar is null)
            return false;

        var downloaded = await DownloadAvatarPicture(uri, cancellationToken).ConfigureAwait(false);
        if (downloaded is null)
            return false;

        var mediaId = MediaId.New(UserId.Value);
        using var processed = await MediaProcessor
            .ProcessUpload(downloaded, MediaKind.UserAvatarPicture, null, cancellationToken)
            .ConfigureAwait(false);
        var mediaRef = await MediaSaver
            .Save(mediaId, processed, false, MediaKind.UserAvatarPicture, cancellationToken)
            .ConfigureAwait(false);

        // Re-check after downloading so we do not overwrite a picture the user changed while the flow was running.
        account = await AccountsBackend.Get(UserId, cancellationToken).ConfigureAwait(false);
        if (account?.IsGuestOrNull() != false)
            return false;

        avatar = await GetDefaultAvatar(account, cancellationToken).ConfigureAwait(false);
        if (avatar is null)
            return false;

        if (avatar.Id.IsEmpty) {
            var change = Change.Create(new AvatarDiff {
                UserId = UserId,
                Name = account.Name,
                MediaId = mediaRef.MediaId,
                PictureUrl = "",
                AvatarKey = "",
            });
            avatar = await Commander
                .Call(new AvatarsBackend_Change(Symbol.Empty, null, change), cancellationToken)
                .ConfigureAwait(false);

            var kvas = ServerKvasBackend.ForUser(UserId);
            var settings = await kvas.UserAvatarSettings().Get(cancellationToken).ConfigureAwait(false);
            settings = settings.WithAvatarId(avatar.Id) with { DefaultAvatarId = avatar.Id };
            await kvas.UserAvatarSettings().Set(settings, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await Commander
            .Call(new AvatarsBackend_Change(
                avatar.Id,
                avatar.Version,
                Change.Update(new AvatarDiff {
                    MediaId = mediaRef.MediaId,
                    PictureUrl = "",
                    AvatarKey = "",
                })),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<AvatarFull?> GetDefaultAvatar(AccountFull account, CancellationToken cancellationToken)
    {
        var userId = account.Id;
        var avatar = account.Avatar.Id.IsEmpty
            ? new AvatarFull(userId).WithMissingPropertiesFrom(account.Avatar)
            : await AvatarsBackend.Get(account.Avatar.Id, cancellationToken).ConfigureAwait(false);
        if (avatar is null)
            return null;

        var defaultAvatarKey = DefaultUserPicture.GetAvatarKey(userId.Value);
        return avatar is {
            MediaId: null,
            PictureUrl.Length: 0,
            AvatarKey: var avatarKey,
        } && avatarKey == defaultAvatarKey
            ? avatar
            : null;
    }

    private async Task<UploadedStreamFile?> DownloadAvatarPicture(Uri uri, CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        HttpResponseMessage response;
        try {
            var client = HttpClientFactory.CreateClient(HttpClientName);
            response = await client.GetAsync(uri, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) {
            Console.Log($"Google picture download failed: {e.Message}");
            return null;
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
            return null;

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!MediaTypeExt.SupportedAvatarContentTypes.Contains(contentType))
            return null;

        if (response.Content.Headers.ContentLength is > Constants.Attachments.AvatarPictureFileSizeLimit)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        if (bytes.Length is 0 or > Constants.Attachments.AvatarPictureFileSizeLimit)
            return null;

        var fileExtension = MediaTypeExt.GetFileExtension(contentType) ?? ".img";
        var file = new UploadedStreamFile(
            $"google-avatar{fileExtension}",
            contentType,
            bytes.Length,
            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
        return file;
    }
}
