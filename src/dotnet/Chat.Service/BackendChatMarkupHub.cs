using ActualChat.Localization;

namespace ActualChat.Chat;

/// <summary>
/// Backend implementation of markup hub for parsing and formatting chat messages.
/// </summary>
public sealed class BackendChatMarkupHub(IServiceProvider services, ChatId chatId) : IBackendChatMarkupHub
{
    private static MarkupTrimmer? _trimmer;
    private static IMarkupFormatter? _editorHtmlConverter;
    private BackendChatMentionResolver? _chatMentionResolver;
    private MentionResolver? _mentionResolver;

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

    public SystemEntryMarkupBuilder SystemEntryMarkupBuilder
        // Main rather than a reader's language: a link preview is cached per content id and an LLM
        // prompt has no reader. Code composing text for a person gets a builder per reader instead.
        => LocalizedSystemEntryMarkupBuilder.Get(Languages.Main);
    public EmptyEntryMarkupBuilder EmptyEntryMarkupBuilder
        => LocalizedEmptyEntryMarkupBuilder.Get(Languages.Main);
}
