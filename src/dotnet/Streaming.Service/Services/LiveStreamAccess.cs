namespace ActualChat.Streaming.Services;

/// <summary>
/// Resolves the chat that owns a live stream and checks the caller's read
/// permissions in it. Shared by the audio, transcript and video entry points
/// so their rules cannot drift apart.
/// </summary>
public sealed class LiveStreamAccess(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAudioStreamingBackend AudioBackend => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private IVideoStreamingBackend VideoBackend => field ??= Services.GetRequiredService<IVideoStreamingBackend>();

    public async Task<bool> CanReadAudio(Session session, StreamId streamId, CancellationToken cancellationToken)
    {
        var chatId = await AudioBackend.GetChatId(streamId, cancellationToken).ConfigureAwait(false);
        return await Can(session, chatId, ChatPermissions.ReadAudio, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CanReadVideo(Session session, StreamId streamId, CancellationToken cancellationToken)
    {
        var chatId = await VideoBackend.GetChatId(streamId, cancellationToken).ConfigureAwait(false);
        return await Can(session, chatId, ChatPermissions.ReadVideo, cancellationToken).ConfigureAwait(false);
    }

    public async Task RequireReadAudio(Session session, StreamId streamId, CancellationToken cancellationToken)
    {
        if (!await CanReadAudio(session, streamId, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions(ChatPermissions.ReadAudio);
    }

    public async Task RequireReadVideo(Session session, StreamId streamId, CancellationToken cancellationToken)
    {
        if (!await CanReadVideo(session, streamId, cancellationToken).ConfigureAwait(false))
            throw ChatPermissionsExt.NotEnoughPermissions(ChatPermissions.ReadVideo);
    }

    // Private methods

    private async Task<bool> Can(
        Session session,
        ChatId? chatId,
        ChatPermissions permissions,
        CancellationToken cancellationToken)
    {
        // An unresolvable stream has no chat to check against, and reads of it find
        // nothing anyway - so it keeps behaving exactly as an unknown stream did.
        if (chatId is null)
            return true;

        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        return rules.Has(permissions);
    }
}
