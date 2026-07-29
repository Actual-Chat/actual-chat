using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// v2.7 legacy IChats facade. Old clients (version ≤ 2.7.9999) call wire-name
/// <c>"IChats"</c> and the interface-level <see cref="LegacyNameAttribute"/> below
/// routes them here without per-method aliases — method names match
/// <see cref="IChats"/>, only the return shapes are pinned to the v2.7 wire format.
/// </summary>
[LegacyName(nameof(IChats), "2.7.9999")]
public interface ILegacyChats : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<LegacyChatNews?> GetNews(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<LegacyChatTile> GetTile(
        Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken);

    // v2.6 IChats.GetTile overload with entryKind is still callable from old clients,
    // route it here so they get a LegacyChatTile rather than the modern union shape.
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<LegacyChatTile> GetTile(
        Session session, ChatId chatId, int entryKind, Range<long> lidTileRange, CancellationToken cancellationToken);

    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<LegacyChatEntry> OnUpsertEntry(
        LegacyChats_UpsertEntry command, CancellationToken cancellationToken);
}

/// <summary>
/// v2.7 wire-frozen counterpart of <see cref="Chats_UpsertEntry"/>. Same field layout —
/// old clients still send the same MemoryPack bytes — but a distinct .NET type so
/// Commander can register one handler per command without colliding with the modern
/// <see cref="IChats.OnUpsertEntry"/>.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record LegacyChats_UpsertEntry(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long? LocalId
) : ISessionCommand<LegacyChatEntry>, IApiCommand, ISanitized
{
    [DataMember, MemoryPackOrder(3)] public string Text {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, MemoryPackOrder(4)] public Option<long?> RepliedEntryLid { get; init; }
    [DataMember, MemoryPackOrder(11)] public ChatEntryAttachment[] Attachments { get; init; } = [];
    [DataMember, MemoryPackOrder(12)] public bool HasUploadingAttachments { get; init; }
    [DataMember, MemoryPackOrder(13)] public string ClientId { get; init; } = "";
    [DataMember, MemoryPackOrder(14)] public ChatEntryForwarded? Forwarded { get; init; }

    public Chats_UpsertEntry ToModern()
        => new(Session, ChatId, LocalId) {
            Text = Text,
            RepliedEntryLid = RepliedEntryLid,
            Attachments = Attachments,
            HasUploadingAttachments = HasUploadingAttachments,
            ClientId = ClientId,
            Forwarded = Forwarded,
        };
}
