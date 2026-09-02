# TypeScript API Index

This document lists public TypeScript exports from the ActualChat client-side codebase.
See also: [C# Full API Index](api-index-full.md), [Condensed API Index](api-index.md).


## ActualLab Core (`src/nodejs/src/actuallab-core`)

- `Disposable` (interface) - Synchronous dispose pattern similar to .NET's IDisposable.
- `AsyncDisposable` (interface) - Asynchronous dispose pattern similar to .NET's IAsyncDisposable.
- `DisposableBag` (class) - Aggregates multiple disposables and disposes them together.
- `LogRef` (interface) - Reference to an object with logging identity tracking.
- `LogScopeFns` (type) - Type for log scope helper functions.
- `LogLevel` (enum) - Log verbosity level (Debug, Info, Warn, Error, None).
- `Log` (class) - Core logging system with scope-based configuration.
- `createLogProvider` (function) - Creates a typed log provider for a package namespace.
- `initLogging` (function) - Initialize logging system and restore persisted levels.
- `LogLevelController` (class) - Runtime control for log level adjustments.
- `IResult<T>` (interface) - Common interface for value-or-error outcomes.
- `Result<T>` (class) - Immutable value-or-error container.
- `result` (function) - Factory function to create a Result with value.
- `errorResult` (function) - Factory function to create a Result with error.
- `resultFrom` (function) - Wrap a function result in Result type.
- `resultFromAsync` (function) - Wrap an async function result in Result type.
- `AsyncContextKey<T>` (class) - Key for storing values in async contexts.
- `AsyncContext` (class) - Async-local storage for context values.
- `abortSignalKey` (const) - Context key for AbortSignal values.
- `AsyncLock` (class) - Provides mutual exclusion for async operations.
- `EventHandlerSet<T>` (class) - Manages a set of event handlers with subscription.
- `PromiseSource<T>` (class) - Promise with externally accessible resolve/reject.
- `resolvedVoidPromise` (const) - Pre-resolved void promise for optimization.
- `RingBuffer<T>` (class) - Circular buffer with fixed-size storage.
- `RetryDelaySeq` (class) - Generates retry delays with exponential backoff.
- `RetryDelay` (interface) - Configuration for a single retry delay period.
- `RetryDelayNone` (const) - Retry delay indicating no delay.
- `RetryDelayLimitExceeded` (const) - Retry delay when limit exceeded.
- `RetryDelayer` (class) - Manages retry delay sequences.


## ActualLab RPC (`src/nodejs/src/actuallab-rpc`)

- `RpcLogScope` (type) - Union of all RPC logging scopes.
- `getLogs` (function) - Get RPC-scoped logger.
- `RpcCallTypeId` (enum) - Type identifier for RPC call categories.
- `RpcSystemCalls` (const) - Named system call identifiers.
- `ENVELOPE_DELIMITER` (const) - Delimiter between envelope and arguments.
- `ARG_DELIMITER` (const) - Delimiter between RPC arguments.
- `FRAME_DELIMITER` (const) - Delimiter between messages in frames.
- `RpcMessage` (interface) - Wire format for RPC messages.
- `serializeMessage` (function) - Serialize message to wire format.
- `serializeFrame` (function) - Serialize frame to wire format.
- `splitFrame` (function) - Split wire frame into individual messages.
- `deserializeMessage` (function) - Deserialize message from wire format.
- `serializeBinaryMessage` (function) - Serialize message to binary format.
- `deserializeBinaryMessage` (function) - Deserialize message from binary.
- `splitBinaryFrame` (function) - Split binary frame into messages.
- `serializeBinaryFrame` (function) - Serialize binary frame.
- `createBinaryEncoder` (function) - Create a binary message encoder.
- `defaultBinaryEncoder` (const) - Default binary encoder instance.
- `defaultBinaryDecoder` (const) - Default binary decoder instance.
- `WebSocketLike` (interface) - WebSocket-compatible interface.
- `RpcConnection` (interface) - RPC connection abstraction.
- `RpcReceivedMessage` (type) - Union of received message types.
- `WebSocketState` (const) - WebSocket state constants.
- `RpcWebSocketConnection` (class) - RPC connection over WebSocket.
- `RpcMessageChannelConnection` (class) - RPC connection via MessageChannel.
- `createMessageChannelPair` (function) - Create paired MessageChannel connections.
- `RpcMethodDef` (interface) - Definition of an RPC method.
- `RpcServiceDef` (interface) - Definition of an RPC service.
- `RpcMethodDefInput` (interface) - Input for RPC method definitions.
- `RpcType` (const) - RPC type markers.
- `RpcRemoteExecutionMode` (const) - Remote execution mode flags.
- `defineRpcService` (function) - Define an RPC service with methods.
- `wireMethodName` (function) - Generate wire-format method name.
- `RpcOutboundCall` (class) - Tracks an outbound RPC call.
- `RpcOutboundCallTracker` (class) - Tracks multiple outbound calls.
- `RpcInboundCall` (class) - Tracks an inbound RPC call.
- `RpcInboundCallTracker` (class) - Tracks multiple inbound calls.
- `RpcCallStage` (const) - RPC call lifecycle stage constants.
- `RpcError` (class) - Exception for RPC errors.
- `IncreasingSeqCompressor` (const) - Compresses sequences of increasing numbers.
- `RpcSystemCallHandler` (class) - Handles RPC system calls.
- `RpcSystemCallSender` (class) - Sends RPC system calls.
- `RpcObjectId` (interface) - Identifier for RPC objects.
- `IRpcObject` (interface) - Base interface for RPC objects.
- `RpcObjectKind` (const) - Kind marker for RPC objects (Local/Remote).
- `RpcStream` (class) - Streaming RPC data container.
- `RpcStreamRef` (type) - Reference to an RPC stream.
- `RpcStreamOptions` (interface) - Options for RPC streams.
- `RpcStreamSource` (type) - Source for RPC stream data.
- `parseStreamRef` (function) - Parse RPC stream reference.
- `resolveStreamRefs` (function) - Resolve stream references in data.
- `RpcStreamSender` (class) - Sends RPC streams.
- `RpcRemoteObjectTracker` (class) - Tracks remote RPC objects.
- `RpcSharedObjectTracker` (class) - Tracks shared RPC objects.
- `RpcPeer` (class) - Base RPC peer implementation.
- `RpcConnectionState` (type) - RPC connection state.
- `RpcClientPeer` (class) - Client-side RPC peer.
- `RpcServerPeer` (class) - Server-side RPC peer.
- `RPC_CLOSE_CODE_UNSUPPORTED_FORMAT` (const) - WebSocket close code for format mismatch.
- `HANDSHAKE_TIMEOUT_MS` (const) - Handshake timeout in milliseconds.
- `RemoteHandshake` (interface) - Remote peer handshake data.
- `RpcCallOptions` (interface) - Options for RPC calls.
- `RpcConnectionUrlResolver` (type) - Function to resolve connection URL.
- `defaultConnectionUrlResolver` (function) - Default URL resolver.
- `RpcHub` (class) - Central hub for managing RPC peers and services.
- `RpcPeerFactory` (type) - Factory function for creating RPC peers.
- `RpcPeerRefBuilder` (class) - Builds RPC peer references.
- `RpcServiceHost` (class) - Hosts RPC service implementations.
- `RpcServiceImpl` (interface) - RPC service implementation contract.
- `RpcDispatchContext` (interface) - Context for RPC method dispatch.
- `createRpcClient` (function) - Create a typed RPC client.
- `WebSocketServer` (interface) - Server-side WebSocket interface.
- `rpcService` (function) - Decorator for RPC service classes.
- `rpcMethod` (function) - Decorator for RPC methods.
- `getServiceMeta` (function) - Get metadata for RPC service.
- `getMethodsMeta` (function) - Get metadata for RPC methods.
- `MethodMeta` (interface) - Metadata for an RPC method.
- `ServiceMeta` (interface) - Metadata for an RPC service.
- `RpcClientPeerReconnectDelayer` (class) - Manages client reconnection delays.
- `RpcPeerState` (interface) - State of an RPC peer.
- `RpcPeerStateKind` (enum) - RPC peer state values.
- `isConnected` (function) - Check if peer state indicates connection.
- `likelyConnected` (function) - Check if peer is likely connected.
- `getStateDescription` (function) - Get human-readable state description.
- `RpcPeerStateMonitor` (class) - Monitors RPC peer connection state.
- `RpcSerializationFormat` (class) - Base serialization format class.
- `RpcSerializationFormatResolver` (class) - Resolves serialization formats.
- `RpcJsonSerializationFormat` (class) - JSON serialization implementation.
- `RpcMessagePackSerializationFormat` (class) - MessagePack serialization.
- `RpcMessagePackCompactSerializationFormat` (class) - Compact MessagePack format.
- `RpcDeserializedMessage` (interface) - Deserialized RPC message.
- `RpcWireData` (type) - Wire data representation.
- `RpcMethodRegistry` (class) - Registry of RPC method handlers.
- `xxh3_64` (function) - 64-bit xxHash3 hash function.
- `xxh3_64str` (function) - 64-bit xxHash3 for strings.
- `computeMethodHash` (function) - Compute hash for RPC method.
- `serializeCompactBinaryMessage` (function) - Serialize compact binary message.
- `deserializeCompactBinaryMessage` (function) - Deserialize compact binary.
- `splitCompactBinaryFrame` (function) - Split compact binary frame.


## API Layer (`src/nodejs/src/api`)

- `Api` (class) - Central API gateway and RPC hub coordinator.
- `WorkerKind` (enum) - Identifies worker/peer type (UI, VideoPlayback, Recording, VideoCapture).
- `ApiModule` (interface) - Loadable API module with dependencies.
- `SessionTokenProvider` (type) - Function providing session tokens.
- `ApiConnectivityUI` (interface) - Connectivity status from .NET side.
- `ApiInitOptions` (interface) - Initialization options for API.
- `ApiReconnectDelayer` (class) - Manages API reconnection backoff.
- `SystemPropertiesDef` (const) - RPC service definition for system properties.
- `ServerApiInfoDto` (interface) - DTO for server API information.
- `SystemPropertiesClient` (interface) - Client for system properties RPC.
- `coreApi` (const) - Default core API module instance.
- `Int64` (type) - 64-bit integer representation.
- `toInt64` (function) - Convert number to Int64.
- `int64ToNumber` (function) - Convert Int64 to number.
- `Moment` (type) - Timestamp as Int64 (ticks).
- `toMoment` (function) - Convert ticks to Moment.
- `momentToNumber` (function) - Convert Moment to number.
- `secondsToMoment` (function) - Convert seconds to Moment.
- `momentToSeconds` (function) - Convert Moment to seconds.
- `VideoFrameDto` (interface) - Video frame data transfer object.
- `VideoFormatDto` (interface) - Video format specification.
- `VideoLatencyReportDto` (interface) - Video latency metrics.
- `VideoLatencyReportResponseDto` (interface) - Response to latency report.
- `AudioFrameDto` (interface) - Audio frame data transfer object.
- `VideoQualityLevelUltra` (const) - Quality level constant.
- `VideoQualityLevelFull` (const) - Quality level constant.
- `VideoQualityLevelHigh` (const) - Quality level constant.
- `VideoQualityLevelMedium` (const) - Quality level constant.
- `VideoQualityLevelLow` (const) - Quality level constant.
- `streamingApi` (const) - Default streaming API module instance.
- `UploadsDef` (const) - RPC service definition for uploads.
- `UploadsAppendCommand` (interface) - Command to append to upload.
- `UploadsClient` (interface) - Client for uploads RPC.
- `uploadsApi` (const) - Default uploads API module instance.


## Main Thread Utilities (`src/nodejs/src`)

- `APP_NAME` (const) - Application name constant.
- `PROD_HOST` (const) - Production host constant.
- `AUDIO_REC` (const) - Audio recording constants.
- `AUDIO_PLAY` (const) - Audio playback constants.
- `AUDIO_ENCODER` (const) - Audio encoder constants.
- `AUDIO_STREAMER` (const) - Audio streamer constants.
- `AUDIO_RECORDER_HEARTBEAT` (const) - Audio recorder heartbeat interval.
- `AUDIO_VAD` (const) - Voice activity detection constants.
- `AudioPlaybackState` (type) - Playback state ('playing' | 'paused' | 'ended' | 'starving').
- `AudioSyncState` (interface) - State of audio playback synchronization.
- `AUDIO_SYNC_KIND_CLEAR` (const) - Audio sync message kind for clear.
- `AUDIO_SYNC_KIND_STATE` (const) - Audio sync message kind for state.
- `AUDIO_PLAYBACK_STATE_CODES` (const) - Mapping of playback states to codes.
- `decodePlaybackState` (function) - Decode playback state from code.
- `AudioSyncMessage` (type) - Wire format for audio sync messages.
- `AudioVideoSyncClient` (class) - Client for audio/video synchronization.
- `RpcNoWait` (type) - Marker for fire-and-forget RPC calls.
- `rpcNoWait` (const) - Symbol for no-wait RPC.
- `RpcTimeout` (interface) - Timeout specification for RPC.
- `RpcCall` (class) - RPC method call with arguments.
- `RpcResult` (class) - RPC call result (value or error).
- `RpcPromise<T>` (class) - Promise-like RPC result holder.
- `completeRpc` (function) - Complete an RPC with result.
- `isTransferable` (function) - Check if value is Transferable.
- `rpcSendNoWait` (function) - Send fire-and-forget RPC.
- `rpcServer` (function) - Create RPC server instance.
- `rpcClient` (function) - Create RPC client instance.
- `rpcClientServer` (function) - Create bidirectional RPC.
- `ServerClock` (class) - Manages server-provided clock synchronization.
- `ServiceWorker` (class) - Service worker registration and messaging.
- `Theme` (class) - Application theme management.
- `ThemeInfo` (interface) - Theme information.
- `nextTick` (const) - Next microtask execution.
- `nextTickAsync` (function) - Async next microtask execution.
- `Timeout` (class) - Timer with disposal capability.
- `PreciseTimeout` (class) - High-precision timeout using performance.now().
- `TimerQueueTimer` (class) - Timer managed by timer queue.
- `TimerQueue` (class) - Custom timer queue implementation.
- `timerQueue` (const) - Global timer queue instance.
- `setTimeout` (const) - Timer queue or native setTimeout.
- `clearTimeout` (const) - Timer queue or native clearTimeout.
- `Versioning` (class) - Version information and comparison.
- `FontSizes` (class) - Font size management and scaling.
- `Vector2D` (class) - 2D vector with arithmetic operations.
- `clamp` (function) - Clamp number to min/max range.
- `lerp` (function) - Linear interpolation between values.
- `RunningCounter` (interface) - Interface for running statistical counters.
- `RunningAverage` (class) - Running average calculation.
- `RunningUnitMedian` (class) - Running median for unit values.
- `RunningMA` (class) - Running moving average.
- `RunningEMA` (class) - Running exponential moving average.
- `RunningMax` (class) - Running maximum value tracker.
- `KaiserBesselDerivedWindow` (function) - Compute Kaiser-Bessel window.
- `approximateGain` (function) - Approximate audio gain from PCM.
- `translate` (function) - Translate value between ranges.
- `average` (function) - Compute average of array.
- `ObjectPool<T>` (class) - Object pool for recycling instances.
- `AsyncObjectPool<T>` (class) - Async object pool.
- `OnDeviceAwake` (class) - On-device wake-up detection.
- `TimedOut` (class) - Sentinel for timeout events.
- `isPromise` (function) - Type guard for Promise-like objects.
- `PromiseSource<T>` (class) - Promise with external resolve/reject.
- `PromiseSourceWithTimeout<T>` (class) - Promise with timeout capability.
- `Cancelled` (type) - Marker for cancellation.
- `cancelled` (const) - Cancellation sentinel value.
- `OperationCancelledError` (class) - Exception for cancelled operations.
- `delayAsync` (function) - Delay execution for duration.
- `delayAsyncWith` (function) - Delay and return value.
- `preciseDelayAsync` (function) - Precise delay using performance.now().
- `flexibleDelayAsync` (function) - Flexible delay with dynamic timeout.
- `ResettableFunc<T>` (interface) - Function with reset capability.
- `ThrottleMode` (type) - Throttle mode ('default' | 'skip' | 'delayHead').
- `throttle` (function) - Throttle function calls.
- `debounce` (function) - Debounce function calls.
- `serialize` (function) - Serialize async function execution.
- `AsyncLockReleaser` (class) - RAII lock releaser.
- `AsyncLock` (class) - Async mutual exclusion primitive.
- `ResolvedPromise` (class) - Promise-like resolved value holder.
- `Resettable` (interface) - Interface for resettable objects.
- `isResettable` (function) - Type guard for Resettable.
- `RetryDelayFn` (type) - Function computing retry delay.
- `expRetryDelays` (function) - Generate exponential retry delays.
- `ResilientStreamOptions<T>` (interface) - Options for resilient streams.
- `resilientStream` (function) - Create resilient async iterable stream.
- `areWasmResourcesLikelyCached` (function) - Check WASM resource cache status.
- `Kvas` (class) - Key-value storage layer.
- `MainThreadDiagnosticsOptions` (interface) - Options for diagnostics.
- `MainThreadDiagnostics` (class) - Collects main thread diagnostics.
- `GestureEvent` (type) - Union of gesture event types.
- `Gestures` (class) - Gesture recognition and handling.
- `Gesture` (class) - Individual gesture tracker.
- `Interactive` (class) - Interactive element utilities.
- `hasModifierKey` (function) - Check for keyboard modifiers.
- `isEscapeKey` (function) - Check if key is Escape.
- `unselect` (function) - Clear text selection.
- `EventHandlerSet<T>` (class) - Event handler management.
- `stopEvent` (function) - Stop event propagation and default.
- `preventDefaultForEvent` (function) - Prevent event default action.
- `tryPreventDefaultForEvent` (function) - Safely prevent default action.
- `Callback` (type) - Parameterless function type.
- `FastRafOptions` (interface) - Options for fastRaf.
- `fastRaf` (function) - Schedule with RequestAnimationFrame.
- `fastReadRaf` (function) - Schedule read phase in RAF.
- `fastWriteRaf` (function) - Schedule write phase in RAF.
- `LogLevel` (enum) - Log severity level.
- `Log` (class) - Logging system instance.
- `LogLevelController` (class) - Control log levels at runtime.


## Worklets (`src/nodejs/src/worklets`)

- `WarmUpAudioWorkletProcessor` (class) - Audio worklet for warming up pipeline.


## UI Blazor — Component Utilities (`src/dotnet/UI.Blazor/Components`)

- `hashCode` (function) - Compute hash code for name string.
- `mod` (function) - Modulo operation.
- `getDigit` (function) - Extract digit at index from number.
- `getBoolDigit` (function) - Extract boolean from digit parity.
- `getAngle` (function) - Calculate angle from x, y coordinates.
- `getUnit` (function) - Get unit value from number.
- `getRandomColor` (function) - Get color from number using palette.
- `getContrast` (function) - Determine contrast color (black or white).


## UI Blazor — Avatar (`src/dotnet/UI.Blazor/Components/Avatar`)

- `BeamAvatar` (class) - Beam-style avatar web component.
- `MarbleAvatar` (class) - Marble-pattern avatar web component.
- `SvgCache` (class) - Cache for SVG avatar renders.


## UI Blazor — UI Elements (`src/dotnet/UI.Blazor/Components`)

- `BubbleHost` (class) - Tooltip/bubble positioning host.
- `TimerButtonSvg` (class) - SVG timer button display.
- `CopyTrigger` (class) - Clipboard copy functionality.
- `DelayedInvoker` (class) - Delayed method invocation.
- `DemandUserInteraction` (class) - User interaction demand prompt.
- `ErrorCatSvg` (class) - Error state SVG display.
- `FileUploadPicker` (class) - File selection and upload.
- `MenuHost` (class) - Context menu host.
- `ModalHost` (class) - Modal dialog host.
- `LoadingCatSvg` (class) - Loading state SVG display.
- `PicUpload` (class) - Picture upload handler.
- `QrCode` (class) - QR code generator web component.
- `Share` (class) - Share functionality.
- `SideNav` (class) - Side navigation host.
- `TabPanel` (class) - Tab panel container.
- `TextBox` (class) - Text input box.
- `TextInput` (class) - Text input control.
- `TooltipHost` (class) - Tooltip display host.
- `TotpInput` (class) - TOTP (time-based one-time password) input.


## UI Blazor — Loading Skeletons (`src/dotnet/UI.Blazor/Components/Skeleton`)

- `ChatListSkeleton` (class) - Chat list loading skeleton.
- `ChatViewFooterSkeleton` (class) - Chat view footer skeleton.
- `ChatViewSkeleton` (class) - Chat view loading skeleton.
- `randomIntFromInterval` (function) - Generate random integer in range.
- `MessageWidth` (enum) - Message width skeleton variants.
- `StringHeight` (enum) - String skeleton height variants.
- `HeightAndWidth` (enum) - Dimension skeleton variants.
- `ImageSkeleton` (class) - Image loading skeleton.
- `PlaceMenuButtonSkeleton` (class) - Place menu button skeleton.
- `RoundSkeletonLit` (class) - Round shape loading skeleton.
- `SplashPageSkeleton` (class) - Splash page loading skeleton.
- `StringSkeletonLit` (class) - String placeholder skeleton.
- `TabSkeleton` (class) - Tab loading skeleton.
- `ThinLeftPanelSkeletonLit` (class) - Left panel skeleton.
- `VoxtSkeletonLit` (class) - Voxt-branded skeleton.


## UI Blazor — VirtualList: FiniteList + InfiniteList (`src/dotnet/UI.Blazor/Components/VirtualList`)

- `InertialScroll` (class) - Inertial scrolling physics.
- `Pivot` (interface) - Pivot point for virtual list.
- `Range<T>` (class) - Generic range container.
- `NumberRange` (class) - Numeric range implementation.
- `VirtualListDataQuery` (class) - Data query for virtual list.
- `VirtualListEdge` (enum) - Edge indicators for virtual list.
- `VirtualListItem` (class) - Individual virtual list item.
- `VirtualListRenderState` (interface) - Rendering state for virtual list.
- `VirtualListStatistics` (class) - Performance statistics.
- `VirtualListStickyEdgeState` (interface) - Sticky edge state tracking.
- `FiniteList` (class, `finite-list.ts`) - Known length, uniform item height, real scrollbar.
  Item position is `index * itemSize`, so loading a different window can't move what's on screen.
- `InfiniteList` (class, `infinite-list.ts`) - Unbounded feed: no scrollbar, fixed huge virtual
  space, items held in place by anchoring. The former `VirtualList` class.


## UI Blazor — VisualMedia & YouTube (`src/dotnet/UI.Blazor/Components`)

- `VisualMediaViewer` (class) - Visual media viewer modal.
- `YoutubePlayer` (class) - YouTube video player.


## UI Blazor.App — Audio Player (`src/dotnet/UI.Blazor.App/Components/AudioPlayer`)

- `AudioPlayer` (class) - Main audio player component.
- `OpusDecoderWorker` (interface) - Worker contract for Opus decoding.
- `BufferHandler` (interface) - Audio buffer handler.
- `OpusDecoder` (class) - Opus audio codec decoder.
- `FeederAudioWorklet` (interface) - Worklet contract for audio feeding.
- `FeederAudioWorkletEventHandler` (interface) - Event handler for feeder.
- `FeederState` (interface) - State of audio feeder.
- `BufferState` (type) - Buffer state indicator.
- `PlaybackState` (type) - Audio playback state.
- `FeederAudioWorkletNode` (class) - Web Audio API worklet node.


## UI Blazor.App — Audio Recorder (`src/dotnet/UI.Blazor.App/Components/AudioRecorder`)

- `AudioDiagnosticsState` (class) - Audio diagnostics snapshot.
- `AudioRecorder` (class) - Audio recording component.
- `AudioRingBuffer` (class) - Ring buffer for audio samples.
- `RecorderStateServer` (interface) - Server interface for recorder state.
- `RecorderStateChanged` (type) - Callback for state changes.
- `RecorderState` (interface) - Recorder state interface.
- `OpusMediaRecorder` (class) - Opus-based media recorder.
- `opusMediaRecorder` (const) - Default recorder instance.
- `RecorderStateHub` (class) - Hub for recorder state management.
- `WebMicrophonePermissionHandler` (class) - Microphone permission handling.
- `AudioStream` (class) - Streamed audio data.
- `AudioStreamer` (class) - Streams audio to workers.
- `VoiceActivityKind` (type) - Voice activity change type.
- `VoiceActivityChange` (interface) - Voice activity detection event.
- `VoiceActivityDetector` (interface) - Voice activity detector contract.
- `NO_VOICE_ACTIVITY` (const) - No voice activity sentinel.
- `AudioVadWorker` (interface) - Worker contract for VAD.
- `WebRtcVoiceActivityDetector` (class) - WebRTC VAD implementation.
- `NeuralVoiceActivityDetector` (class) - Neural network VAD.
- `OpusEncoderWorker` (interface) - Worker contract for Opus encoding.
- `ResamplerWrapper` (class) - Audio resampler wrapper.
- `ResamplerLoader` (class) - Loader for resampler library.
- `WorkerConnectivityUI` (class) - Connectivity status for workers.
- `AudioVadWorklet` (interface) - Worklet contract for VAD.
- `AudioVadProcessorOptions` (interface) - Options for VAD worklet.
- `AudioVadWorkletProcessor` (class) - VAD audio worklet processor.
- `OpusEncoderWorklet` (interface) - Worklet contract for Opus encoding.
- `OpusEncoderProcessorOptions` (interface) - Options for encoder worklet.
- `OpusEncoderWorkletProcessor` (class) - Opus encoder audio worklet.


## UI Blazor.App — Chat Components (`src/dotnet/UI.Blazor.App/Components`)

- `ChatActivityPanel` (class) - Chat activity display panel.
- `ActiveRecordingSvg` (class) - Active recording indicator SVG.
- `NarrowRecordingSvg` (class) - Narrow recording indicator SVG.
- `RecorderToggle` (class) - Recording toggle control.
- `AttachmentWebFilePickerRegistry` (class) - Registry for file picker backends.
- `PickFileResult` (interface) - File picker result.
- `AttachmentWebFilePickerBackend` (class) - File picker backend.
- `AttachmentWebFilePicker` (class) - Web-based file picker.
- `PanelMode` (type) - Chat editor panel mode.
- `ChatMessageEditor` (class) - Message composition editor.
- `FilePreviews` (class) - File attachment preview manager.
- `ChatEntryMessageInternalView` (class) - Internal message view.
- `DateVisor` (class) - Date separator display.
- `EmojiModal` (class) - Emoji selection modal.
- `FontSizeSlider` (class) - Font size adjustment control.
- `JoinVideoCallModal` (class) - Video call join dialog.
- `MarkupEditor` (class) - Markup/markdown editor.
- `UndoStack<T>` (class) - Undo/redo stack implementation.
- `highlightCode` (function) - Syntax highlight code block.
- `PlayableTextMarkupView` (class) - Playable text markup display.
- `MentionList` (class) - User mention suggestions.
- `SortableList` (class) - Sortable list implementation.
- `clickFileInput` (function) - Trigger file input click.
- `createBlobUrlFromInput` (function) - Create blob URL from input.
- `clearFileInput` (function) - Clear file input value.
- `PicCropModal` (class) - Picture crop dialog.
- `RightPanelHeader` (class) - Right panel header.
- `SearchPanel` (class) - Chat search functionality.
- `SelectionHost` (class) - Selection state management.
- `SubHeader` (class) - Sub-header display.


## UI Blazor.App — Video Panel (`src/dotnet/UI.Blazor.App/Components/VideoPanel`)

- `PresentableFrame` (interface) - Renderable video frame.
- `RenderBackendKind` (type) - Render backend type identifier.
- `RenderBackend` (interface) - Video render backend interface.
- `CanvasRenderBackend` (class) - Canvas-based rendering.
- `isOffThreadPlausible` (function) - Check for off-thread rendering support.
- `OffThreadRenderBackend` (class) - Off-thread rendering backend.
- `renderQualityLevelForWidth` (function) - Determine quality from width.
- `SpatialLayerConfig` (interface) - Simulcast spatial layer configuration.
- `hasHigherTopTier` (function) - Check spatial layer tier.
- `MAX_SIMULCAST_TIERS` (const) - Maximum simulcast tiers.
- `LadderBuildInput` (interface) - Input for simulcast ladder.
- `buildLadderForSource` (function) - Build simulcast layer configuration.
- `OwnStreamDiagnosticsSnapshot` (interface) - Own stream diagnostics.
- `collectOwnStreamDiagnostics` (function) - Collect own stream metrics.
- `VideoDebugSettings` (interface) - Video debugging configuration.
- `getVideoDebugSettings` (function) - Get debug settings.
- `setVideoDebugForceH264Only` (function) - Force H.264 codec.
- `VideoPanel` (class) - Video streaming panel.
- `getActivePlayers` (function) - Get active video players.
- `RemoteStreamDiagnostics` (interface) - Remote stream diagnostics.
- `VideoPlayer` (class) - Remote video player.
- `OwnStreamDiagnostics` (interface) - Own stream diagnostic info.
- `VideoDevice` (interface) - Video device information.
- `getActiveRecorder` (function) - Get active video recorder.
- `getAllActiveRecorders` (function) - Get all active recorders.
- `ActiveRecorderListener` (type) - Callback for recorder changes.
- `addActiveRecorderListener` (function) - Subscribe to recorder changes.
- `PreviewFrameListener` (type) - Callback for preview frames.
- `VideoRecordingState` (type) - Recording state indicator.
- `VideoRecorder` (class) - Video recording component.
- `VideoStreamingPreview` (class) - Video streaming preview display.
