using ActualChat.Internal;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Serialization;

#pragma warning disable CA2255

public static class CoreSerializerAndRpcSetup
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // This is super important: TypeRef and some other types which were formerly using Symbol
        // are stored in our DB, and this option enables their legacy serialization mode.
        StringAsSymbolMemoryPackFormatterAttribute.IsEnabled = true;
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Language>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<StreamId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MediaId>());
        // Principal identifiers + RoleId
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PrincipalId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AuthorId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<RoleId>());
        // Chat identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PeerChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<GroupChatId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceChatId>());
        // Chat entry identifiers
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ChatEntryId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TextEntryId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<AudioEntryId>());
        // Other chat-related
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<PlaceId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<MentionId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ConversationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<TranslationId>());
        // Other user-related
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<ExternalContactId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<NotificationId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserDeviceId>());
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<UserLinkId>());
        // Everything else
        MemoryPackFormatterProvider.Register(new StringIdentifierMemoryPackFormatter<Phone>());
    }

    public static void Configure(bool isServer)
    {
        RpcSerializationFormat.All = ImmutableList.Create(
            RpcSerializationFormat.SystemJsonV3,
            RpcSerializationFormat.MemoryPackV2,
            RpcSerializationFormat.MemoryPackV2C,
            RpcSerializationFormat.MemoryPackV2NP,
            RpcSerializationFormat.MemoryPackV2CNP,
            RpcSerializationFormat.MemoryPackV3,
            RpcSerializationFormat.MemoryPackV3C);

        RpcSerializationFormatResolver.Default = RpcSerializationFormatResolver.Default with {
            DefaultClientFormatKey =
#if DEBUG
                "mempack3",
#else
                isServer ? "mempack3" : "mempack3c",
#endif
        };
    }
}
