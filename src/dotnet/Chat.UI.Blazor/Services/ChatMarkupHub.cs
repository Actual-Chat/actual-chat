using ActualChat.Chat.UI.Blazor.Services.Internal;
using ActualChat.Search;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMarkupHub(IServiceProvider services, ChatId chatId) : IChatMarkupHub
{
    private IMarkupParser? _parser;
    private static IMarkupTrimmer? _trimmer;
    private IChatMentionResolver? _mentionResolver;
    private ISearchProvider<MentionSearchResult>? _mentionSearchProvider;
    private IMentionNamer? _mentionNamer;
    private static IMarkupFormatter? _editorHtmlConverter;

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;

    public IMarkupParser Parser
        => _parser ??= Services.GetRequiredService<IMarkupParser>();

#pragma warning disable CA1822 // Can be static
    public IMarkupTrimmer Trimmer
        => _trimmer ??= new MarkupTrimmer();
#pragma warning restore CA1822

    public IMentionNamer MentionNamer
        => _mentionNamer ??= new MentionNamer(MentionResolver);

    public IChatMentionResolver MentionResolver
        => _mentionResolver ??= new ChatMentionResolver(Services, ChatId);

    public ISearchProvider<MentionSearchResult> MentionSearchProvider
        => _mentionSearchProvider ??= new ChatMentionSearchProvider(Services, ChatId);

    public IMarkupFormatter EditorHtmlConverter
        => _editorHtmlConverter ??= new MarkupEditorHtmlConverter();
}
