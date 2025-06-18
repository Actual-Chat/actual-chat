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
        if (localUrl.IsChat(out var chatId)) {
            var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is not null) {
                var contentId = new ContentId(ContentKind.Chat, chatId);
                return await ContentLinksBackend
                    .GetContentInfo(contentId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        return null;
    }
}
