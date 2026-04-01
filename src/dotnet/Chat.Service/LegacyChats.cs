namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 IChats implementation for backward compatibility.
/// Delegates to real IChats and converts results to legacy types.
/// Remove once all clients are migrated past v2.6.
/// </summary>
#pragma warning disable CS0618 // Obsolete
public class LegacyChats(IServiceProvider services) : ILegacyChats
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<LegacyChatNews?> GetLegacyNews(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var news = await Chats.GetNews(session, chatId, cancellationToken).ConfigureAwait(false);
        return LegacyChatNews.From(news);
    }

    public virtual async Task<LegacyChatTile> GetLegacyTile(
        Session session, ChatId chatId, int entryKind, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var tile = await Chats.GetTile(session, chatId, idTileRange, cancellationToken).ConfigureAwait(false);
        return LegacyChatTile.From(tile);
    }

    public virtual async Task<LegacyChatEntry> OnLegacyUpsertTextEntry(
        Chats_UpsertTextEntry command, CancellationToken cancellationToken)
    {
        ChatEntryForwarded? forwarded = null;
        if (command.ForwardedChatEntryId != null || command.ForwardedAuthorId != null)
            forwarded = new ChatEntryForwarded {
                ChatEntryId = command.ForwardedChatEntryId,
                AuthorId = command.ForwardedAuthorId ?? null!,
                ChatTitle = command.ForwardedChatTitle ?? "",
                AuthorName = command.ForwardedAuthorName ?? "",
                BeginsAt = command.ForwardedChatEntryBeginsAt ?? default,
            };
        var newCommand = new Chats_UpsertEntry(command.Session, command.ChatId, command.LocalId) {
            Text = command.Text,
            RepliedEntryLid = command.RepliedEntryLid,
            Attachments = command.EntryAttachments,
            HasUploadingAttachments = command.HasAttachmentUploads,
            ClientId = command.ClientId,
            Forwarded = forwarded,
        };
        var entry = await Commander.Call(newCommand, true, cancellationToken).ConfigureAwait(false);
        return LegacyChatEntry.From(entry);
    }
}
#pragma warning restore CS0618
