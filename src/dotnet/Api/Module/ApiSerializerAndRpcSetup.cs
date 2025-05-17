using ActualChat.Internal;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Module;

#pragma warning disable CA2255

public static class ApiSerializerAndRpcSetup
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // Rpc API version
        RpcDefaults.ApiVersion = RpcDefaults.BackendVersion = Constants.Api.Version;
        // This is super important: TypeRef and some other types that were formerly using Symbol
        // are stored in our DB, and this option enables their legacy serialization mode.
        StringAsSymbolMemoryPackFormatterAttribute.IsEnabled = true;

        // Custom MemoryPack formatters

        // Common / general identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AliasId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MediaId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<StreamId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Language>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Phone>());
        // Principal identifiers + RoleId
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PrincipalId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AuthorId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<RoleId>());
        // User-related
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ExternalContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<NotificationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ExplicitNotificationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserDeviceId>());
        // Chat identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PeerChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<GroupChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceChatId>());
        // Chat entry identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatEntryId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TextEntryId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AudioEntryId>());
        // Other chat-related identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Emoji>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Language>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MentionId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ConversationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TranslationId>());
        // Roulette identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatRouletteId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Country>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Interest>());
    }
}
