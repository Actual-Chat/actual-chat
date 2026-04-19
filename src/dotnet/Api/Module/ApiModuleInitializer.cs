using ActualChat.Aot;
using ActualChat.Internal;
using ActualLab.Serialization.Internal;
using MessagePack;
using MessagePack.Resolvers;

namespace ActualChat.Module;

/// <summary>
/// Module initializer that registers MemoryPack formatters for identifiers.
/// </summary>
#pragma warning disable CA2255

public static class ApiModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        AotTypes.AddSource(new ApiAotSource());
        // This is super important: TypeRef and some other types that were formerly using Symbol
        // are stored in our DB, and this option enables their legacy serialization mode.
        StringAsSymbolMemoryPackFormatterAttribute.IsEnabled = true;

        // Prepend MessagePack resolvers that supply caching formatters for VideoFrame and
        // AudioFrame — enables serialize-once fan-out via the frame's SerializedData.
        // Scoped to VideoFrame / AudioFrame only. Must run before the first frame is resolved
        // (module-initializer timing guarantees this).
        DefaultMessagePackResolver.Resolvers = [
            ActualChat.Video.CachingVideoFrameResolver.Instance,
            ActualChat.Audio.CachingAudioFrameResolver.Instance,
            StandardResolver.Instance,
        ];

        // Custom MemoryPack formatters

        // Fixed lists
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Emoji>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Country>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Interest>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Language>());
        // Common / general identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AliasId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MediaId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<StreamId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Phone>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Email>());
        // Principal identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PrincipalId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AuthorId>());
        // User-related
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<RoleId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ExternalContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<NotificationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ExplicitNotificationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserDeviceId>());
        // Chat identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PeerChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<GroupChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ThreadChatId>());
        // Chat entry identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatEntryId>());
        // Other chat-related identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MentionId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ConversationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TranslationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TranslationSourceId>());
        // Content identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ContentId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UploadId>());
    }
}
