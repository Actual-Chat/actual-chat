using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// v2.7 legacy IChats facade. Old clients (version ≤ 2.7.9999) call wire-name
/// <c>"IChats"</c> and the RPC layer routes them here via <see cref="LegacyNameAttribute"/>.
/// Methods convert the modern union-shaped responses to <see cref="LegacyChatEntry"/>
/// and friends so the on-wire MemoryPack format old clients expect is preserved.
/// </summary>
[LegacyName(nameof(IChats), "2.7.9999")]
public interface ILegacyChats : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    [LegacyName(nameof(IChats.GetNews), "2.7.9999")]
    Task<LegacyChatNews?> GetLegacyNews(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    [LegacyName(nameof(IChats.GetTile), "2.7.9999")]
    Task<LegacyChatTile> GetLegacyTile(
        Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken);

    [LegacyName(nameof(IChats.OnUpsertEntry), "2.7.9999"), RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    Task<LegacyChatEntry> OnLegacyUpsertEntry(
        Chats_UpsertEntry command, CancellationToken cancellationToken);
}
