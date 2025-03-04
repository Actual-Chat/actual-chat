using ActualChat.Search;

namespace ActualChat.Chat;

public class BackendChatMarkupHub(IServiceProvider services, ChatId chatId) : IBackendChatMarkupHub
{
    private static MarkupTrimmer? _trimmer;
    private BackendChatMentionResolver? _mentionResolver;
    private MentionNamer? _mentionNamer;
    private static IMarkupFormatter? _editorHtmlConverter;

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;
    [field: AllowNull, MaybeNull]
    public IMarkupParser Parser
        => field ??= Services.GetRequiredService<IMarkupParser>();

#pragma warning disable CA1822
    public IMarkupTrimmer Trimmer
        => _trimmer ??= new MarkupTrimmer();
#pragma warning restore CA1822

    public IMentionNamer MentionNamer
        => _mentionNamer ??= new MentionNamer(MentionResolver);

    public IChatMentionResolver MentionResolver
        => _mentionResolver ??= new BackendChatMentionResolver(Services, ChatId);

    public ISearchProvider<MentionSearchResult> MentionSearchProvider
        => throw StandardError.Internal($"You should use {nameof(IChatMarkupHub)} to get {nameof(MentionSearchProvider)}.");

    public IMarkupFormatter EditorHtmlConverter
        => _editorHtmlConverter ??= new MarkupEditorHtmlConverter();
}
