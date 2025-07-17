using ActualChat.Hashing;

namespace ActualChat.Chat;

public abstract class TranslationSource(TranslationSourceId sourceId)
{
    public TranslationSourceId TranslationSourceId { get; } = sourceId;
    public abstract HashString ContentHash { get; }
    public abstract string Content { get; }
}

internal class TextEntryTranslationSource(ChatEntry entry, TranslationSourceId sourceId) : TranslationSource(sourceId)
{
    public ChatEntry ChatEntry { get; } = entry;
    public override HashString ContentHash => ChatEntry.ContentHash;
    public override string Content => ChatEntry.Content;
}

internal class ConversationTranslationSource(Conversation conversation, TranslationSourceId sourceId)
    : TranslationSource(ValidateKind(sourceId))
{
    private static TranslationSourceId ValidateKind(TranslationSourceId sourceId)
    {
        var kind = sourceId.Kind;
        var isValid = kind is TranslationIdKind.ConversationTitle
            or TranslationIdKind.ConversationDescription
            or TranslationIdKind.ConversationSummary;
        if (!isValid)
            throw new ArgumentOutOfRangeException(nameof(sourceId),
                "Only conversation translation id should be provided.");

        return sourceId;
    }

    public Conversation Conversation { get; } = conversation;

    // Surrogate hash.
    // If Conversation is updated => version id is updated, and we consider that we need to update translations.
    public override HashString ContentHash => new HashString(HashAlgorithm.None,
        HashEncoding.Base64,
        Conversation.Version.ToInvariantString().ToBase64());

    public override string Content => TranslationSourceId.Kind switch {
        TranslationIdKind.ConversationTitle => Conversation.Title,
        TranslationIdKind.ConversationDescription => Conversation.Description,
        TranslationIdKind.ConversationSummary => Conversation.Summary,
        _ => throw new NotSupportedException()
    };
}

internal class ThreadTranslationSource : TranslationSource
{
    public ThreadTranslationSource(Chat treadChat, TranslationSourceId sourceId)
        :base(ValidateKind(sourceId))
        => ThreadChat = treadChat;

    private static TranslationSourceId ValidateKind(TranslationSourceId sourceId)
    {
        var kind = sourceId.Kind;
        var isValid = kind is TranslationIdKind.ThreadTitle
            or TranslationIdKind.ThreadDescription;
        if (!isValid)
            throw new ArgumentOutOfRangeException(nameof(sourceId),
                "Only thread translation id should be provided.");

        return sourceId;
    }

    public Chat ThreadChat { get; }

    // Surrogate hash.
    // If ThreadChat is updated => version id is updated, and we consider that we need to update translations.
    public override HashString ContentHash => new HashString(HashAlgorithm.None,
        HashEncoding.Base64,
        ThreadChat.Version.ToInvariantString().ToBase64());

    public override string Content => TranslationSourceId.Kind switch {
        TranslationIdKind.ThreadTitle => ThreadChat.Title,
        TranslationIdKind.ThreadDescription => ThreadChat.Description,
        _ => throw new NotSupportedException()
    };
}
