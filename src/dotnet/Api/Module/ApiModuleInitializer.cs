using ActualChat.Audio;
using ActualChat.Internal;
using ActualChat.Video;

namespace ActualChat.Module;

#pragma warning disable CA2255

/// <summary>
/// API module initializer that registers MessagePack formatters and MessagePack converters
/// for a number of types declared in this assembly.
/// </summary>
public static partial class ApiModuleInitializer
{
    /// <summary>
    /// IStringLike identifier types owned by ActualChat.Api. Single source of truth — used both
    /// for the MemoryPack formatter list below and the Nerdbank converter registration.
    /// </summary>
    private static readonly Type[] StringLikeIdentifiers = [
        // Fixed lists
        typeof(Emoji), typeof(Country), typeof(Interest), typeof(Language),
        // Common / general identifiers
        typeof(AliasId), typeof(MediaId), typeof(StreamId), typeof(Phone), typeof(Email),
        typeof(LocalUrl),
        // Principal identifiers
        typeof(PrincipalId), typeof(UserId), typeof(AuthorId), typeof(UserIdentity),
        // User-related
        typeof(RoleId), typeof(ContactId), typeof(ExternalContactId),
        typeof(NotificationId), typeof(ExplicitNotificationId), typeof(UserDeviceId),
        // Chat identifiers
        typeof(PlaceId), typeof(ChatId), typeof(PeerChatId), typeof(GroupChatId),
        typeof(PlaceChatId), typeof(ThreadChatId),
        // Chat entry identifiers
        typeof(ChatEntryId),
        // Other chat-related identifiers
        typeof(MentionId), typeof(ConversationId),
        typeof(TranslationId), typeof(TranslationSourceId),
        // Content identifiers
        typeof(ContentId), typeof(UploadId),
    ];

    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        CoreModuleInitializer.Load();
        // AotTypes.AddSource + Serializers.RegisterShapeProvider(ApiWitness.Generated…) are
        // handled by the generated ApiModuleInitializer.g.cs half's RegisterGenerated.
        // Union dispatch goes through PolyType's native [DerivedTypeShape] attributes on each
        // union base (see MediaFrame, LiveStreamItem, StoredSettings) — Nerdbank picks them up
        // from the codegen shape (so PolyType's reflection-emit dispatcher, which has a
        // one-byte-branch overflow for large unions, is bypassed).
        Serializers.RegisterStringLikeTypes(StringLikeIdentifiers);

        // Hand-written serialize-once-fan-out converters for AudioFrame / VideoFrame.
        // Both wire variants (Key-honoring and keyless) get the same map layout — application
        // code never sees the auto-generated converter for these types.
        Serializers.RegisterConverters([
            CachingAudioFrameFormatter.Instance,
            CachingVideoFrameFormatter.Instance,
        ]);
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
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<MentionId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ConversationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<TranslationId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<TranslationSourceId>());
        // Content identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ContentId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UploadId>());
    }
}
