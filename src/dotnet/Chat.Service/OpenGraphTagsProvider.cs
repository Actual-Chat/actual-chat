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
        ContentRef? contentRef = null;
        if (localUrl.IsChat(out var chatId, out long entryLid)) {
            var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is not null) {
                if (entryLid > 0) {
                    var chatEntryId = ChatEntryId.New(chatId, entryLid);
                    var chatEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
                    if (chatEntry is not null)
                        contentRef = chatEntryId.ContentRef;
                }
                else
                    contentRef = chatId.ContentRef;
            }
        }
        if (contentRef is not null)
            return await ContentLinksBackend
                .GetContentInfo(contentRef, cancellationToken)
                .ConfigureAwait(false);

        return null;
    }
}
