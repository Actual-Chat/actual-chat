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
        Session session, ChatId chatId, int entryKind, Range<long> idTileRange, CancellationToken cancellationToken);

    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<LegacyChatEntry> OnUpsertEntry(
        Chats_UpsertEntry command, CancellationToken cancellationToken);
}
