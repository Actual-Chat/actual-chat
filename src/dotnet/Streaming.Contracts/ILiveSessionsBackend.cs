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
    Task<LiveSessionState?> Get(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<LiveSession?> GetLiveSession(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsParticipant(ChatId chatId, UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsForcedMuted(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken);

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
    Task SetRules(ChatId chatId, SessionRules rules, CancellationToken cancellationToken);
    Task MutePeer(ChatId chatId, AuthorId targetAuthorId, bool muted, CancellationToken cancellationToken);
    Task UpdateSummary(
        ChatId chatId,
        LiveSessionSummary summary,
        CancellationToken cancellationToken);
}
