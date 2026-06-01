using ActualChat.Aot;
using ActualChat.Internal;

namespace ActualChat.Module;

/// <summary>
/// Module initializer that registers MemoryPack formatters for identifiers.
/// </summary>
#pragma warning disable CA2255

public static partial class ApiModuleInitializer
{
    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        CoreModuleInitializer.Load();
        AotTypes.AddSource(new ApiAotSource());

        // This is super important: TypeRef and some other types that were formerly using Symbol
        // are stored in our DB, and this option enables their legacy serialization mode.
        StringAsSymbolMemoryPackFormatterAttribute.IsEnabled = true;

        // Custom MemoryPack formatters

        // Fixed lists
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Emoji>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Country>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Interest>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Language>());
        // Common / general identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<AliasId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<MediaId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<StreamId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Phone>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Email>());
        // Principal identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<PrincipalId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UserId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<AuthorId>());
        // User-related
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<RoleId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ContactId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ExternalContactId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<NotificationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ExplicitNotificationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UserDeviceId>());
        // Chat identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<PlaceId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ChatId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<PeerChatId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<GroupChatId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<PlaceChatId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ThreadChatId>());
        // Chat entry identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ChatEntryId>());
        // Other chat-related identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<MentionRef>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ConversationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<TranslationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<TranslationSourceId>());
        // Content identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ContentId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UploadId>());
    }
}
