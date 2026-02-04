# Live Hub: Multiplexed Streaming Service

## Goal
Replace the current RealtimeChatPlayer's multi-call architecture with a single multiplexed RPC stream to minimize latency.

## Current Architecture (Problem)
```
Client                          Server
  │                               │
  ├─ ChatEntryReader.Observe() ──►│ (Fusion computed - watches entries)
  │◄── Entry with StreamId ───────┤
  │                               │
  ├─ GetAudio(streamId1) ────────►│ (RPC call per stream)
  │◄── RpcStream<byte[]> ─────────┤
  │                               │
  ├─ GetAudio(streamId2) ────────►│ (another RPC call)
  │◄── RpcStream<byte[]> ─────────┤
```
**Latency sources**: Multiple round-trips, entry observation delay, separate stream setup per speaker.

## New Architecture (Solution)
```
Client                          Server
  │                               │
  ├─ GetLiveStream(config)──────────►│ (single RPC call)
  │◄── RpcStream<LiveItem> ───────┤ (multiplexed: control + audio frames)
  │    [StreamStarted id=1]       │
  │    [AudioFrame id=1 data=...] │
  │    [StreamStarted id=2]       │
  │    [AudioFrame id=1 data=...] │
  │    [AudioFrame id=2 data=...] │
  │    ...interleaved...          │
```

## Implementation Plan

### Phase 1: Common Abstractions (`src/dotnet/Core/Channels/`)

#### 1.1 Stream Muxer/Demuxer (generic, reusable)
```csharp
// ChannelMuxer.cs - combines multiple Channel<T> into one
public class ChannelMuxer<T> : IAsyncDisposable
{
    public ChannelReader<T> Output { get; }
    public void AddSource(ChannelReader<T> source);
    public void RemoveSource(ChannelReader<T> source);
}

// ChannelDemuxer.cs - splits one channel into multiple by key
public class ChannelDemuxer<TKey, TItem> : IAsyncDisposable
{
    public void SetInput(ChannelReader<TItem> input);
    public ChannelReader<TItem> GetChannel(TKey key);
}
```

### Phase 2: Domain Models (`src/dotnet/Api/Live/`)

#### 2.1 Base Item Type
```csharp
// LiveItem.cs - polymorphic base for all stream items
[DataContract, MemoryPackable, MemoryPackUnion(0, typeof(LiveAudioFrame))]
[MemoryPackUnion(1, typeof(LiveStreamStart))]
[MemoryPackUnion(2, typeof(LiveStreamEnd))]
public abstract partial record LiveItem;
```

#### 2.2 Derived Item Types
```csharp
// LiveAudioFrame.cs - audio data packet
[DataContract, MemoryPackable]
public sealed partial record LiveAudioFrame(
    [property: DataMember(Order = 0)] int StreamIndex,
    [property: DataMember(Order = 1)] byte[] Data
) : LiveItem;

// LiveStreamStart.cs - new stream announcement
[DataContract, MemoryPackable]
public sealed partial record LiveStreamStart(
    [property: DataMember(Order = 0)] int StreamIndex,
    [property: DataMember(Order = 1)] Moment BeginsAt,
    [property: DataMember(Order = 2)] AuthorId AuthorId,
    [property: DataMember(Order = 3)] ChatEntryId EntryId,
    [property: DataMember(Order = 4)] AudioFormat Format
) : LiveItem;

// LiveStreamEnd.cs - stream completed
[DataContract, MemoryPackable]
public sealed partial record LiveStreamEnd(
    [property: DataMember(Order = 0)] int StreamIndex
) : LiveItem;
```

#### 2.3 Configuration Model
```csharp
// LiveStreamingSettings.cs
[DataContract, MemoryPackable]
public sealed partial record LiveStreamingSettings(
    [property: DataMember(Order = 0)] bool IsListening = true,
    [property: DataMember(Order = 1)] LiveStreamKind Kinds = LiveStreamKind.Audio
);

[Flags]
public enum LiveStreamKind { None = 0, Audio = 1, /* Future: Video = 2 */ }
```

### Phase 3: Contracts (`src/dotnet/Api.Contracts/Live/`)

#### 3.1 Server Interface
```csharp
// ILiveHub.cs
public interface ILiveHub : IRpcService
{
    Task<RpcStream<LiveItem>> GetStream(
        Session session,
        ChatId chatId,
        LiveStreamingSettings config,
        CancellationToken cancellationToken);

    Task UpdateConfig(
        Session session,
        ChatId chatId,
        LiveStreamingSettings config,
        CancellationToken cancellationToken);
}
```

### Phase 4: Server Implementation (`src/dotnet/Streaming.Service/`)

#### 4.1 LiveHub (Frontend Service)
```csharp
// Services/LiveHub.cs
public class LiveHub : ILiveHub
{
    public async Task<RpcStream<LiveItem>> GetStream(...)
    {
        // Validate, create muxer, return RpcStream.New()
    }
}
```

#### 4.2 LiveStreamMuxer
```csharp
// Services/LiveStreamMuxer.cs
public class LiveStreamMuxer : IAsyncDisposable
{
    private readonly Channel<LiveItem> _output;
    private readonly ChannelMuxer<LiveItem> _muxer;

    // Watches chat entries, for each streaming entry:
    // - Emit LiveStreamStart
    // - Subscribe to audio stream, wrap frames as LiveAudioFrame
    // - Emit LiveStreamEnd when done

    public ChannelReader<LiveItem> Output => _output.Reader;
    public void UpdateConfig(LiveStreamingSettings config);
}
```

### Phase 5: Client Implementation (`src/dotnet/UI.Blazor.App/Services/Live/`)

#### 5.1 LiveStreamDemuxer
```csharp
// LiveStreamDemuxer.cs
public class LiveStreamDemuxer : IAsyncDisposable
{
    public event Action<LiveStreamStart, IAsyncEnumerable<byte[]>> StreamStarted;
    public event Action<int> StreamEnded;

    public async Task RunAsync(RpcStream<LiveItem> input, CancellationToken ct);
}
```

### Phase 6: Update RealtimeChatPlayer

Replace ChatEntryReader + GetAudio() calls with LiveStreamDemuxer consumption.

## File Structure
```
src/dotnet/
├── Core/Channels/
│   ├── ChannelMuxer.cs
│   └── ChannelDemuxer.cs
├── Api/Live/
│   ├── LiveItem.cs
│   ├── LiveAudioFrame.cs
│   ├── LiveStreamStart.cs
│   ├── LiveStreamEnd.cs
│   └── LiveStreamingSettings.cs
├── Api.Contracts/Live/
│   └── ILiveHub.cs
├── Streaming.Service/Services/
│   ├── LiveHub.cs
│   └── LiveStreamMuxer.cs
└── UI.Blazor.App/Services/Live/
    └── LiveStreamDemuxer.cs
```

## Implementation Order
1. Channel helpers in Core (ChannelMuxer, ChannelDemuxer)
2. Domain models (Api/Live/)
3. Contract interface (Api.Contracts/Live/ILiveHub.cs)
4. Server muxer (Streaming.Service)
5. Client demuxer (UI.Blazor.App)
6. Update RealtimeChatPlayer
7. Register services in DI modules
