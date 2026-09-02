using ActualChat.UI.Blazor.App.Services.Internal;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatMarkupHub(IServiceProvider services, ChatId chatId) : IChatMarkupHub
{
    private static IMarkupTrimmer? _trimmer;
    private static IMarkupFormatter? _editorHtmlConverter;

    private ChatId NonThreadChatId => ChatId.GetThreadOutermostParentOrSelf();

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;

    public IMarkupParser Parser
        => field ??= Services.GetRequiredService<IMarkupParser>();

#pragma warning disable CA1822 // Can be static
    public IMarkupTrimmer Trimmer
        => _trimmer ??= new MarkupTrimmer();
#pragma warning restore CA1822

    public IMentionResolver MentionResolver
        => field ??= new MentionResolver(ChatMentionResolver);

    public IChatMentionResolver ChatMentionResolver
        => field ??= new ChatMentionResolver(Services, NonThreadChatId);

    public IMarkupFormatter EditorHtmlConverter
        => _editorHtmlConverter ??= MarkupEditorHtmlConverter.Instance;

    public SystemEntryMarkupBuilder SystemEntryMarkupBuilder
        => field ??= Services.GetRequiredService<SystemEntryMarkupBuilder>();

    public EmptyEntryMarkupBuilder EmptyEntryMarkupBuilder
        => field ??= Services.GetRequiredService<EmptyEntryMarkupBuilder>();
}
