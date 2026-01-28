using ActualChat.Attributes;
using ActualChat.Hosting;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Streaming;

[BackendService(nameof(HostRole.VideoBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.VideoBackend))]
public interface IRealtimeStreamingBackend : IComputeService, IBackendService
{
    // State queries (no session check)
    [ComputeMethod]
    Task<ActiveVideoStreams> GetActiveVideoStreams(ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<VideoStreamInfo?> GetVideoStreamInfo(StreamId streamId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken);

    // Event subscription
    Task<RpcStream<VideoStreamEvent>> SubscribeToVideoStreamEvents(ChatId chatId, CancellationToken cancellationToken);

    // Viewer count
    [ComputeMethod]
    Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken);

    // Commands (for stream registration)
    [CommandHandler]
    Task OnRegisterVideoStream(RealtimeStreamingBackend_RegisterVideoStream command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnUnregisterVideoStream(RealtimeStreamingBackend_UnregisterVideoStream command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnRegisterVideoStreamMember(RealtimeStreamingBackend_RegisterVideoStreamMember command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnUnregisterVideoStreamMember(RealtimeStreamingBackend_UnregisterVideoStreamMember command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RealtimeStreamingBackend_RegisterVideoStream(
    [property: DataMember, MemoryPackOrder(0)] VideoStreamInfo StreamInfo
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => StreamInfo.ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RealtimeStreamingBackend_UnregisterVideoStream(
    [property: DataMember, MemoryPackOrder(0)] StreamId StreamId,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RealtimeStreamingBackend_RegisterVideoStreamMember(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] string SessionId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RealtimeStreamingBackend_UnregisterVideoStreamMember(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] string SessionId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public ChatId ShardKey => ChatId;
}
