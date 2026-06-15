using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ILiveSessionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<LiveConversation?> Get(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LiveSession?> GetLiveSession(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsParticipant(ChatId chatId, UserId userId, CancellationToken cancellationToken);

    Task OnStreamRegistered(
        ChatId chatId,
        AuthorId authorId,
        long? entryLid,
        bool transcriptionOn,
        CancellationToken cancellationToken);
    Task OnStreamsChanged(ChatId chatId, CancellationToken cancellationToken);
    Task Close(ChatId chatId, CancellationToken cancellationToken);
    Task SetParticipation(
        ChatId chatId,
        UserId userId,
        ParticipationKind kind,
        bool isActive,
        CancellationToken cancellationToken);
    Task SetMicMuted(
        ChatId chatId,
        UserId userId,
        bool micMuted,
        CancellationToken cancellationToken);
    Task UpdateSummary(
        ChatId chatId,
        LiveConversationSummary summary,
        CancellationToken cancellationToken);
}
