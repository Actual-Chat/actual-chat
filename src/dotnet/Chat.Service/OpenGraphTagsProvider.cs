namespace ActualChat.Chat;

public class OpenGraphTagsProvider(IServiceProvider services)
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IContentLinksBackend ContentLinksBackend { get; } = services.GetRequiredService<IContentLinksBackend>();

    public async Task<ContentLinkInfo?> GetContentLinkInfo(
        Session session,
        LocalUrl localUrl,
        CancellationToken cancellationToken)
    {
        ContentId? contentId = null;
        if (localUrl.IsChat(out var chatId, out long entryLid)) {
            var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is not null) {
                if (entryLid > 0) {
                    var textEntryId = TextEntryId.New(chatId, entryLid);
                    var chatEntry = await Chats.GetEntry(session, textEntryId, cancellationToken).ConfigureAwait(false);
                    if (chatEntry is not null)
                        contentId = ContentId.New(textEntryId);
                }
                else
                    contentId = ContentId.New(chatId);
            }
        }
        if (contentId is not null)
            return await ContentLinksBackend
                .GetContentInfo(contentId, cancellationToken)
                .ConfigureAwait(false);
        return null;
    }
}
