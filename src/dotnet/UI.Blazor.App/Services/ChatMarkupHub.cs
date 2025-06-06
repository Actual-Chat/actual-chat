using ActualChat.UI.Blazor.App.Services.Internal;
using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatMarkupHub(IServiceProvider services, ChatId chatId) : IChatMarkupHub
{
    private static IMarkupTrimmer? _trimmer;
    private static IMarkupFormatter? _editorHtmlConverter;

    private ChatId NonThreadChatId => ChatId.GetThreadOutermostParentOrSelf();

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;

    [field: AllowNull, MaybeNull]
    public IMarkupParser Parser
        => field ??= Services.GetRequiredService<IMarkupParser>();

#pragma warning disable CA1822 // Can be static
    public IMarkupTrimmer Trimmer
        => _trimmer ??= new MarkupTrimmer();
#pragma warning restore CA1822

    [field: AllowNull, MaybeNull]
    public IMentionNamer MentionNamer
        => field ??= new MentionNamer(MentionResolver);

    [field: AllowNull, MaybeNull]
    public IChatMentionResolver MentionResolver
        => field ??= new ChatMentionResolver(Services, NonThreadChatId);

    [field: AllowNull, MaybeNull]
    public ISearchProvider<MentionSearchResult> MentionSearchProvider
        => field ??= new ChatMentionSearchProvider(Services, NonThreadChatId);

    public IMarkupFormatter EditorHtmlConverter
        => _editorHtmlConverter ??= new MarkupEditorHtmlConverter();
}
