using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 IChats service for backward compatibility.
/// Routes old clients (version ≤ 2.6.9999) to these methods via version-based RPC routing.
/// Remove once all clients are migrated past v2.6.
/// </summary>
[LegacyName("IChats", "2.6.9999")]
[Obsolete("2025.03: Legacy v2.6 compat service")]
public interface ILegacyChats : IComputeService
{
    // Old clients call IChats.GetNews:3 → returns ChatNews with ChatEntry (incompatible)
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    [LegacyName("GetNews", "2.6.9999")]
    Task<LegacyChatNews?> GetLegacyNews(
        Session session, ChatId chatId, CancellationToken cancellationToken);

    // Old clients call IChats.GetTile:5 (with entryKind) → returns ChatTile with ChatEntry[] (incompatible)
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    [LegacyName("GetTile", "2.6.9999")]
    Task<LegacyChatTile> GetLegacyTile(
        Session session, ChatId chatId, int entryKind, Range<long> idTileRange, CancellationToken cancellationToken);

    // Old clients call IChats.OnUpsertTextEntry:2 → returns ChatEntry (incompatible)
    [CommandHandler, RpcMethod(ConnectTimeout = double.PositiveInfinity)]
    [LegacyName("OnUpsertTextEntry", "2.6.9999")]
    Task<LegacyChatEntry> OnLegacyUpsertTextEntry(Chats_UpsertTextEntry command, CancellationToken cancellationToken);
}
