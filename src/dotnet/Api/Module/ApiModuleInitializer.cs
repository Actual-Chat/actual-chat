using ActualChat.Aot;
using ActualChat.Internal;
using ActualChat.Serialization.Internal;

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

        // Union roots that tolerate a member this build has no tag for. The shared Formatters
        // table covers both the keyed and the keyless resolver in one registration, and both
        // consult it before their own resolver chains.
        // StoredSettings is deliberately absent: tolerance alone wouldn't fix its actual bug,
        // which is that an unreadable row is dropped on write-back.
        RegisterForwardCompatibleUnion<ChatEntry>();
        RegisterForwardCompatibleUnion<SystemEntry>();
        RegisterForwardCompatibleUnion<Markup>();
        RegisterForwardCompatibleUnion<Notifications.Notification>();
        RegisterForwardCompatibleUnion<Invite.Invite>();

        // Custom MemoryPack formatters.
        // Only identifiers reachable from the two remaining MemoryPack read paths are registered:
        // legacy flow state (Core.Server/Flows/FlowData.cs) and legacy server KVAS values
        // (Core/Kvas/KvasSerializer.cs). Everything else is MessagePack-only now.

        // Flow cursor identifiers
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UserId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ChatId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ChatEntryId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ContactId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<PlaceId>());
        // Other types reachable from flow state
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<AuthorId>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<MediaId>());
        // Stored settings
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<Language>());
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<UploadId>());
        // External contact hashing
        MemoryPackFormatterProvider.Register(new StringLikeMemoryPackFormatter<ExternalContactId>());
    }

    // Private methods

    private static void RegisterForwardCompatibleUnion<TBase>()
        where TBase : class, IForwardCompatibleUnion<TBase>
        => AppMessagePackResolverSettings.Formatters[typeof(TBase)] =
            typeof(ForwardCompatibleUnionFormatter<TBase>);
}
