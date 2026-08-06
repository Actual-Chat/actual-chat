
namespace ActualChat.Chat;

/// <summary>
/// Backend implementation of markup hub for parsing and formatting chat messages.
/// </summary>
public class BackendChatMarkupHub(IServiceProvider services, ChatId chatId) : IBackendChatMarkupHub
{
    private static MarkupTrimmer? _trimmer;
    private BackendChatMentionResolver? _chatMentionResolver;
    private MentionResolver? _mentionResolver;
    private static IMarkupFormatter? _editorHtmlConverter;

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;
    public IMarkupParser Parser
        => field ??= Services.GetRequiredService<IMarkupParser>();

#pragma warning disable CA1822
    public IMarkupTrimmer Trimmer
        => _trimmer ??= new MarkupTrimmer();
#pragma warning restore CA1822

    public IMentionResolver MentionResolver
        => _mentionResolver ??= new MentionResolver(ChatMentionResolver);

    public IChatMentionResolver ChatMentionResolver
        => _chatMentionResolver ??= new BackendChatMentionResolver(Services, ChatId);

    public IMarkupFormatter EditorHtmlConverter
        => _editorHtmlConverter ??= MarkupEditorHtmlConverter.Instance;
}
