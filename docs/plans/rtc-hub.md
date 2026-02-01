# RTC Hub: Multiplexed Streaming Service

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
  ├─ GetStream(config) ──────────►│ (single RPC call)
  │◄── RpcStream<RtcItem> ────────┤ (multiplexed: control + audio frames)
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

### Phase 2: Domain Models (`src/dotnet/Api/Rtc/`)

#### 2.1 Base Item Type
```csharp
// RtcItem.cs - polymorphic base for all stream items
[DataContract, MemoryPackable, MemoryPackUnion(0, typeof(RtcAudioFrame))]
[MemoryPackUnion(1, typeof(RtcStreamStart))]
[MemoryPackUnion(2, typeof(RtcStreamEnd))]
public abstract partial record RtcItem;
```

#### 2.2 Derived Item Types
```csharp
// RtcAudioFrame.cs - audio data packet
[DataContract, MemoryPackable]
public sealed partial record RtcAudioFrame(
    [property: DataMember(Order = 0)] int StreamIndex,
    [property: DataMember(Order = 1)] byte[] Data
) : RtcItem;

// RtcStreamStart.cs - new stream announcement
[DataContract, MemoryPackable]
public sealed partial record RtcStreamStart(
    [property: DataMember(Order = 0)] int StreamIndex,
    [property: DataMember(Order = 1)] Moment BeginsAt,
    [property: DataMember(Order = 2)] AuthorId AuthorId,
    [property: DataMember(Order = 3)] ChatEntryId EntryId,
    [property: DataMember(Order = 4)] AudioFormat Format
) : RtcItem;

// RtcStreamEnd.cs - stream completed
[DataContract, MemoryPackable]
public sealed partial record RtcStreamEnd(
    [property: DataMember(Order = 0)] int StreamIndex
) : RtcItem;
```

#### 2.3 Configuration Model
```csharp
// RtcStreamConfig.cs
[DataContract, MemoryPackable]
public sealed partial record RtcStreamConfig(
    [property: DataMember(Order = 0)] bool IsListening = true,
    [property: DataMember(Order = 1)] RtcStreamKind Kinds = RtcStreamKind.Audio
);

[Flags]
public enum RtcStreamKind { None = 0, Audio = 1, /* Future: Video = 2 */ }
```

### Phase 3: Contracts (`src/dotnet/Api.Contracts/Rtc/`)

#### 3.1 Server Interface
```csharp
// IRtcHub.cs
public interface IRtcHub : IRpcService
{
    Task<RpcStream<RtcItem>> GetStream(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken);

    Task UpdateConfig(
        Session session,
        ChatId chatId,
        RtcStreamConfig config,
        CancellationToken cancellationToken);
}
```

### Phase 4: Server Implementation (`src/dotnet/Streaming.Service/`)

#### 4.1 RtcHub (Frontend Service)
```csharp
// Services/RtcHub.cs
public class RtcHub : IRtcHub
{
    public async Task<RpcStream<RtcItem>> GetStream(...)
    {
        // Validate, create muxer, return RpcStream.New()
    }
}
```

#### 4.2 RtcStreamMuxer
```csharp
// Services/RtcStreamMuxer.cs
public class RtcStreamMuxer : IAsyncDisposable
{
    private readonly Channel<RtcItem> _output;
    private readonly ChannelMuxer<RtcItem> _muxer;

    // Watches chat entries, for each streaming entry:
    // - Emit RtcStreamStart
    // - Subscribe to audio stream, wrap frames as RtcAudioFrame
    // - Emit RtcStreamEnd when done

    public ChannelReader<RtcItem> Output => _output.Reader;
    public void UpdateConfig(RtcStreamConfig config);
}
```

### Phase 5: Client Implementation (`src/dotnet/UI.Blazor.App/Services/Rtc/`)

#### 5.1 RtcStreamDemuxer
```csharp
// RtcStreamDemuxer.cs
public class RtcStreamDemuxer : IAsyncDisposable
{
    public event Action<RtcStreamStart, IAsyncEnumerable<byte[]>> StreamStarted;
    public event Action<int> StreamEnded;

    public async Task RunAsync(RpcStream<RtcItem> input, CancellationToken ct);
}
```

### Phase 6: Update RealtimeChatPlayer

Replace ChatEntryReader + GetAudio() calls with RtcStreamDemuxer consumption.

## File Structure
```
src/dotnet/
├── Core/Channels/
│   ├── ChannelMuxer.cs
│   └── ChannelDemuxer.cs
├── Api/Rtc/
│   ├── RtcItem.cs
│   ├── RtcAudioFrame.cs
│   ├── RtcStreamStart.cs
│   ├── RtcStreamEnd.cs
│   └── RtcStreamConfig.cs
├── Api.Contracts/Rtc/
│   └── IRtcHub.cs
├── Streaming.Service/Services/
│   ├── RtcHub.cs
│   └── RtcStreamMuxer.cs
└── UI.Blazor.App/Services/Rtc/
    └── RtcStreamDemuxer.cs
```

## Implementation Order
1. Channel helpers in Core (ChannelMuxer, ChannelDemuxer)
2. Domain models (Api/Rtc/)
3. Contract interface (Api.Contracts/Rtc/IRtcHub.cs)
4. Server muxer (Streaming.Service)
5. Client demuxer (UI.Blazor.App)
6. Update RealtimeChatPlayer
7. Register services in DI modules
