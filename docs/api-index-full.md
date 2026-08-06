# API Index (Full)

This document lists notable public types in ActualChat .NET projects.
See also: [Condensed API Index](api-index.md), [TypeScript API Index](api-index-ts.md).


## ActualChat.Core

- `Alphabet` - Defines character sets for string generation (alphanumeric, base64, etc.).
- `AppKind` (enum) - Specifies the application kind (Web, Android, iOS, Windows, MacOS).
- `AvatarKind` (enum) - Specifies the type of avatar (Default, Uploaded, Beam, etc.).
- `Change<T>` (record struct) - Represents a change operation (create, update, remove).
- `ChangeKind` (enum) - Specifies the type of change operation.
- `DeltaText` - Represents a time delta as human-readable text.
- `Email` (record struct) - Represents an email address.
- `Emoji` (record struct) - Represents an emoji character or sequence.
- `ExternalError` - Exception for external service errors.
- `HashAlgorithm` (enum) - Specifies the hash algorithm to use.
- `HashEncoding` (enum) - Specifies the encoding for hash output.
- `HashInput` - Input for hash computation.
- `HashOutput` (struct) - Output of a hash computation.
- `InternalError` - Exception for internal application errors.
- `IRateLimiter<TKey>`, `IRateLimiter<TKey, TBudget>` - Rate limiter contracts (`ActualChat.Resilience`).
- `Interest` (record struct) - Represents a user interest tag.
- `Language` (record struct) - Represents a language identifier (BCP 47).
- `MediaType` (enum) - Specifies the type of media content.
- `MemSearchDocument` (readonly struct) - In-memory search match blob (`ActualChat.Search`): lowercased camelCase/digit-segment tokens; ctor / `IsMatch` / `GetCoverageScore` / `OrNew`.
- `MemSearchQuery` (readonly struct) - Parsed in-memory search query (`ActualChat.Search`): precompiled prefix needles; ctor / `IsMatch` / `GetMatchParts`.
- `NotFoundException` - Exception thrown when an entity is not found.
- `Phone` (record struct) - Represents a phone number in E.164 format.
- `PostponeException` - Exception indicating an operation should be postponed.
- `RandomNameGenerator` - Generates random memorable names from word lists.
- `StandardError` (static class) - Factory methods for creating standard errors.
- `StandardError.Account` (static class) - Account-related error factory methods.
- `StandardError.Chat` (static class) - Chat-related error factory methods.
- `UploadException` - Exception for file upload errors.
- `WrongShardException` - Exception thrown when accessing the wrong shard.
- `AsyncMemoizer<T>` - Memoizes async operation results.
- `CancellingDebouncer<T>` - Debouncer variant with cancellation token support.
- `ChannelDemuxer<TKey, TItem>` - Demultiplexes one channel into multiple by key.
- `ChannelMuxer<TKey, TItem>` - Multiplexes multiple channels into one.
- `Debouncer<T>` - Delays action execution until interval passes without new items.
- `IAsyncObservable<T>` - Observable with channel-based async subscribers.
- `IAsyncSubscription<T>` - Async subscription with async dispose.
- `MaybeHasNext<TItem>` (record struct) - Represents an item with "has next" flag.
- `TaskSerializer` - Serializes task execution to run sequentially.
- `Throttler<T>` - Limits action execution to at most once per interval.
- `LruCache<TKey, TValue>` - Thread-safe LRU cache implementation.
- `ThreadSafeLruCache<TKey, TValue>` - Thread-safe wrapper for LruCache.
- `BlockRingBuffer<T>` - Ring buffer with block-level operations.
- `SimpleConcurrentPool<T>` - Simple concurrent object pool.
- `SharedResourcePool<TResource>` - Pooled resource lease management.
- `IdAndVersionEqualityComparer` - Compares entities by ID and version.
- `Change<TCreate, TUpdate>` (record) - Represents a change with separate create/update types.
- `DiffEngine` - Processes diffs for entity changes.
- `IDiff` - Interface for diff representations.
- `RecordDiff<T>` - Diff for record types.
- `FeatureDef<T>` - Feature flag definition.
- `FeatureDefRegistry` - Registry of feature definitions.
- `Features` - Aggregates client and server feature flags.
- `FeaturesBase` (abstract class) - Base class for feature implementations.
- `IClientFeatures` - Client-side feature flags.
- `IFeatures` - Feature flag access interface.
- `IServerFeatures` - Server-side feature flags.
- `ServerFeatures` - Server feature implementation.
- `HostInfo` (record) - Host environment information.
- `HostKind` (enum) - Host environment type (Server, MauiApp, WasmApp).
- `HostRole` (enum) - Host role (OneServer, OneApiServer, OneBackendServer).
- `IDbInitializer` - Database initialization interface.
- `IModuleInitializer` - Module initialization interface.
- `ModuleHost` - Host for modules.
- `ModuleHostBuilder` - Builder for module hosts.
- `ServiceMode` (enum) - Service operation mode.
- `BatchingKvas` - Batching KVAS with delayed persistence.
- `IBatchingKvasBackend` - Backend for batching KVAS.
- `IKvas` - Key-value store interface.
- `IKvasStore` - An IKvas that owns its storage, so it can be listed, flushed and cleared.
- `KvasarKvas` - Encrypted file-based KVAS store built on ActualLab.Kvasar.
- `IServerKvas` - Server-side KVAS interface.
- `LocalSettings` - Local settings storage via KVAS.
- `PrefixedKvas` - KVAS with key prefix.
- `StoredState<T>` - State backed by KVAS.
- `SyncedState<T>` - Synchronized state.
- `IMessageProcessor<TMessage>` - Queue-based async message processor.
- `ITerminalMessage` - Marker for terminal/final messages.
- `MessageProcess<TMessage>` - Represents a single message processing task.
- `MessageProcessingException` - Exception during message processing.
- `MessageProcessor<TMessage>` - Generic message processor implementation.
- `MessageProcessorBase<TMessage>` - Base class for message processors.
- `Tracer` - Performance tracing.
- `ISearchProvider` - Search functionality interface.
- `SearchMatch` - Search match: matched text, rank, highlight parts (explicit or lazy from a `MemSearchQuery`).
- `SearchMatchPart` - Part of a search match.
- `SearchResult` - Base class for search results.
- `ISecureTokens` - Secure token operations.
- `ISecureTokensBackend` - Backend for secure tokens.
- `SecureToken` - Secure token.
- `SecureValue<T>` - Secure value wrapper.
- `TrueSessionResolver` - Session resolution.
- `Mutable<T>` - Mutable value holder.
- `ThreadSafeMutable<T>` - Thread-safe mutable value holder.
- `ActivatedWorkerBase` (abstract class) - Worker with activation tracking.
- `WorkerBase` (abstract class) - Long-running background worker base.
- `AsyncEnumerableExt` (static class) - Extension methods for IAsyncEnumerable.
- `AsyncEnumerableOnce<T>` - IAsyncEnumerable wrapper that allows single enumeration.
- `AsyncEnumerableWithUsedEnumerator<T>` - IAsyncEnumerable wrapper that tracks enumerator usage.
- `ChannelExt` (static class) - Extension methods for channels.
- `CachingKeyedFactory<TKey, TValue>` - Keyed factory with caching.
- `CastingKeyedFactory<TKey, TValue>` - Keyed factory with type casting.
- `KeyedFactory<TKey, TValue>` - Generic keyed factory.
- `CompositeServiceProvider` - Service provider that combines multiple providers.
- `ConcurrentLruCache<TKey, TValue>` - Concurrent LRU cache implementation.
- `DefaultSessionResolver` - Default session resolver implementation.
- `DelegatingWorker` - Worker that delegates to another worker.
- `FuncWorker` - Worker that executes a function.
- `LazyServiceProvider` - Lazy service provider wrapper.
- `LazyWriter<T>` - Writer with lazy initialization.
- `NoRecursionRegion` - Prevents recursive execution.
- `NonLazyServiceAccessor<T>` - Eager service accessor.
- `NodeRef` (struct) - Reference to a mesh node.
- `IHasNodeRef` - Interface for types with a node reference.
- `IHasOrigin` - Interface for types with an origin.
- `IHasShardKey` - Interface for types with a shard key.
- `IHasDelayQuanta` - Interface for types with delay quanta.
- `IHasDelayUntil` - Interface for types with delay until.
- `IHasKvasKey` - Interface for types with a KVAS key.
- `SafeDisposable` - Safe disposable wrapper.
- `SafeDisposableBase` (abstract class) - Base class for safe disposables.
- `ScopedTracerProvider` - Scoped tracer provider.
- `ScopedKvasProxy` - Scoped KVAS proxy.
- `KvasAccessor<T>` - Typed KVAS accessor.
- `KvasExt` (static class) - Extension methods for IKvas.
- `KvasSerializer` - KVAS serialization utilities.
- `ExpiringEntry<T>` (record) - Entry with expiration time.
- `LogSinks` (static class) - Log sink utilities.
- `TailLogger` - Logger that keeps recent entries.
- `TailLoggerProvider` - Provider for tail loggers.
- `RegionalValue<T>` (record) - Value with regional context.
- `TimeSpanFormatExt` (static class) - TimeSpan formatting extensions.
- `RefUnit` (struct) - Reference type unit value.
- `RunningAverage` - Running average calculation.
- `RunningEma` - Running exponential moving average.
- `RunningUnitMedian` - Running median calculation.
- `Countries` (static class) - Country data utilities.
- `Country` (record) - Country information.
- `Emojis` (static class) - Emoji utilities and lookup.
- `Interests` (static class) - Interest tag utilities.
- `Languages` (static class) - Language utilities and lookup.
- `Bots` (static class) - Bot-related utilities.
- `CoreModule` - Core module configuration.
- `DbInitializer` (abstract class) - Database initializer base.
- `IApiCommand` - Marker interface for API commands.
- `IDispatcherResolver` - Dispatcher resolution interface.
- `IServerSettings` - Server settings interface.
- `IThreadSafeLruCache<TKey, TValue>` - Thread-safe LRU cache interface.
- `LongAsStringKeyComparer` - Comparer for long keys as strings.
- `StringIdentifier<T>` (abstract record struct) - Base for string-based identifiers.
- `SymbolIdentifier<T>` (abstract record struct) - Base for Symbol-based identifiers.
- `SystemRole` (enum) - System role types; `Anyone`/`Guest`/`User`/`AnonymousUser` have automatic membership, `Moderator`/`Owner` have an explicit author list.
- `MetadataExt` (static class) - Extension methods for metadata.
- `AudioFocusService` - Manages audio focus across the app.
- `StreamHub` - Hub for audio/video streams.
- `StreamStore` - Store for stream data.
- `Choice<T>` (record) - Represents a selectable choice.
- `PlaybackCommands` - Playback command definitions.
- `PlayerCommands` - Player command definitions.
- `ServerKvasBackendClient` - Client for server KVAS backend.
- `ServerSettingsKvasClient` - Client for server settings via KVAS.
- `GuestIdOption` (enum) - Guest ID options.
- `LocalStorage` - Local browser storage access.
- `LocalUrl` (record struct) - Represents a local (relative) URL.
- `LocalUrlExt` (static class) - Extension methods for LocalUrl.
- `DisplayUrl` (record) - URL with display properties.
- `BaseUrlKind` (enum) - Specifies the type of base URL.
- `UrlMapper` - Maps between different URL formats.
- `Maybe<T>` (struct) - Optional value wrapper.
- `MappingChannelReader<TIn, TOut>` - Channel reader with mapping.
- `MemorySegment<T>` - Memory segment wrapper.
- `TypeMap` - Maps types for serialization.
- `TypeMapper` - Type mapping utilities.
- `IUnion` - Marker interface for union types.
- `NumericUnion<T>` (struct) - Union type for numeric values.
- `ClientFeatures` - Client-side feature flags.
- `TestFeatures` - Test feature flags.
- `ServerFeaturesClient` - Client-side server features.
- `ISleepDurationProvider` - Provides sleep durations.
- `ILogConsumer` - Log consumer interface.
- `INotFoundException` - Interface for not-found exceptions.
- `CoreConstants` (static class) - Core constants.
- `CoreSettings` (record) - Core settings.
- `EventCommand` (record) - Event-based command.
- `EventHandlerAttribute` (attribute) - Marks event handlers.
- `QueueAttribute` (attribute) - Queue configuration attribute.
- `QueueRef` (record) - Reference to a queue.
- `BackendServiceAttribute` (attribute) - Marks backend services.
- `BackendShardSchemeAttribute` (attribute) - Shard scheme configuration.
- `IWebServerModule` - Web server module interface.
- `ApiModule` - API module.
- `ApiModuleInitializer` - API module initializer.
- `HostModule` - Host module.
- `LegacyLanguageFormatter` - Legacy language formatter.
- `LegacyLanguageFormatterAttribute` (attribute) - Legacy formatter attribute.
- `LegacyNullableLanguageFormatter` - Legacy nullable language formatter.
- `IgnoreComputeArg` - Marks arguments to ignore in compute.
- `LinearMapExt` (static class) - Extension methods for linear maps.
- `MasterFlowStarter` - Starts master flows.
- `ShardSchemeExt` (static class) - Extension methods for shard schemes.
- `RpcDependentReconnectDelayer` - RPC reconnect delayer.
- `SessionInfo` (record) - Session information.
- `SessionInfoExt` (static class) - Extension methods for SessionInfo.
- `SessionEncoding` (static class) - Session encoding utilities.
- `OggCRC32` (static class) - OGG CRC32 calculation.
- `BlobPath` (record struct) - Path to a blob in storage.
- `BlobScope` (enum) - Specifies the scope of blob storage.
- `Crawler` - Web page crawler.
- `MediaRef` (record) - Media content information.
- `MediaProcessor` - Processes media files.
- `MediaSaver` - Saves media files.
- `TranscriptDiffStreamExt` (static class) - Extension methods for transcript diff streams.
- `TranscriberExt` (static class) - Extension methods for transcibers.
- `DeepgramTranscriber` - Deepgram speech-to-text transcriber.
- `GoogleTranscriber` - Google speech-to-text transcriber.
- `AliasId` (struct) - Unique identifier for an alias.
- `AliasInfo` (record) - Alias information.


## ActualChat.Core.Audio

- `AudioProcessingModule` - Web audio API audio processing module wrapper.
- `AudioProcessingModuleConfig` - Configuration for audio processing module.
- `ProcessingConfig` - Audio processing pipeline configuration.
- `DownmixMethod` (enum) - Methods for downmixing audio channels.
- `GainControlMode` (enum) - Gain control modes for APM.
- `NoiseSuppressionLevel` (enum) - Noise suppression intensity levels.
- `VoiceActivityDetector` - Voice activity detection wrapper.
- `NoopVoiceActivityDetector` - No-op voice activity detector.
- `OnnxVoiceActivityDetector` - ONNX-based voice activity detector.
- `VoiceActivityKind` (enum) - Types of voice activity (Start, End).


## ActualChat.Core.Server

- `RateLimitClass` (enum) - Selects the budgets an inbound call is charged against.
- `RateLimitClassResolver` (record) - Maps an inbound RPC command to its `RateLimitClass`.
- `RateLimitBudgets` (record) - Per-class, per-identity-kind call budgets.
- `RateLimitIdentity` (record struct) - One identity dimension a call is charged against.
- `RateLimitPolicy` - Charges an inbound call against every dimension that has a rule; throws `RateLimitExceededException`.
- `RateLimitRule` - One configured limit: a limiter, its budget and its key builder.
- `RateLimitSource` (record struct) - What a call is known to come from (session, IP).
- `RateLimitIdentityResolver` (sealed class) - Fills the identity dimensions of a call.
- `LocalRateLimiter<TKey>` (sealed class) - In-process fixed-window limiter, used for commands.
- `IRateLimitUserIdResolver` - Resolves the user id dimension of a session.

- `IAnthropicClient` - Anthropic Claude API client wrapper.
- `IPromptHelpers` - Prompt-template helper service.
- `PromptTemplate` (record) - Reusable prompt template with named variables.
- `PromptHelpersExt` (static class) - Extensions for IPromptHelpers.
- `BackendServiceDef` (record) - Defines a backend service with hosting role and service mode.
- `BackendServiceDefs` (sealed class) - Registry of all backend service definitions.
- `Content` - Content information.
- `GoogleCloudBlobStorage` - Google Cloud Storage implementation.
- `GoogleCloudBlobStorages` - Factory for Google Cloud storages.
- `IBlobStorage` - Individual blob storage operations.
- `IBlobStorages` - Blob storage service.
- `IContentSaver` - Content saver interface.
- `LocalFolderBlobStorage` - Local folder storage.
- `LocalFolderBlobStorages` - Factory for local folder storages.
- `BatchedIndexingFlow` (abstract class) - Batched indexing flow.
- `BatchIndexingResult` (record) - Batch result.
- `Flow` (abstract class) - Base for long-running operations.
- `Flow<TResult>` (abstract class) - Typed flow with result.
- `FlowAttribute` (attribute) - Flow decoration.
- `FlowConsole` - Flow console output.
- `FlowData` - Flow data.
- `FlowDef` - Flow definition.
- `FlowDefs` - Flow definitions registry.
- `FlowHub` - Hub for flows.
- `FlowRegistry` - Flow registry.
- `FlowResumeEvent` (record) - Flow resume event.
- `FlowRuntime` - Flow runtime.
- `IFlowBackend` - Flow backend.
- `IFlowImpl` - Flow implementation interface.
- `IHasLastRunAt` - Has last run time.
- `IMasterFlow` - Master flow.
- `IndexingFlow` (abstract class) - Indexing with cursor tracking.
- `IndexingFlowCursor` - Indexing cursor.
- `IndexingMasterFlow` (abstract class) - Master indexing flow.
- `PeriodicFlow` (abstract class) - Periodic execution flow.
- `ThrottledFlow` (abstract class) - Throttled flow.
- `ThrottledUpdateFlow` (abstract class) - Throttled update flow.
- `IMeshLocks` - Distributed locking.
- `MeshLockHolder` - Lock holder.
- `MeshLockInfo` (record) - Lock info.
- `MeshLocksBase` (abstract class) - Locks base.
- `MeshLockOptions` (record) - Lock options.
- `MeshLockReleaseResult` (record) - Release result.
- `MeshNode` - Mesh node.
- `MeshNodeState` (record) - Node state.
- `MeshState` (enum) - Mesh state.
- `MeshWatcher` - Watches mesh state changes.
- `CommandKind` (enum) - Command type.
- `InMemoryQueues` - In-memory queues.
- `InMemoryQueueProcessor` - In-memory processor.
- `IQueueProcessor` - Queue processor.
- `IQueues` - Queue service.
- `IQueueSender` - Queue sender.
- `ITimeoutProvider` - Timeout provider.
- `LocalQueueProcessor` - In-memory queue processor.
- `NatsQueues` - NATS message broker queues.
- `NatsQueueProcessor` - NATS queue processor.
- `NatsSettings` - NATS settings.
- `QueuedCommand` (record) - Queued command.
- `QueuesBase` (abstract class) - Base for queues.
- `ShardQueueProcessor` - Shard-aware processor.
- `MeshRef` - Mesh reference.
- `MeshRefResolvers` - Mesh reference resolvers.
- `MeshRpcPeerRef` - RPC peer reference.
- `MeshRpcPeerRefs` - RPC peer references.
- `ShardedDbServiceBase` (abstract class) - Sharded database service base.
- `ShardedDbWorkerBase` (abstract class) - Sharded database worker base.
- `ShardKeyResolvers` - Shard key resolution.
- `ShardMap` - Shard mapping.
- `ShardOwner` - Shard owner.
- `ShardOwners` - Shard owners collection.
- `ShardOwnership` (record) - Ownership information.
- `ShardOwnershipStatus` (enum) - Ownership status.
- `ShardRunnable` (abstract class) - Shard runnable.
- `ShardWorker` - Shard worker.
- `IMediaProcessor` - Media processor.
- `IMediaSaver` - Media saver.
- `IUploadProcessor` - Upload processor.
- `ProcessedFile` (record) - Processed file.
- `UploadedFile` (record) - Uploaded file.
- `UploadedStreamFile` - Uploaded stream file.
- `UploadedTempFile` - Uploaded temp file.
- `IHealthState` - Health state.


## ActualChat.Backend

- `IRequiresRandomShard` - Marker interface indicating a service requires a random shard.
- `IRequiresThisNode` - Marker interface indicating a service must run on the current node.
- `IRequiresZeroShard` - Marker interface indicating a service requires the zero shard.
- `ShardScheme` - Sharding scheme configuration.
- `ShardSchemeFlags` (enum) - Flags configuring a shard scheme's behavior.
- `ServerHashInputExt` (static class) - Server-side hash input extensions.


## ActualChat.Db

- `DbModule` - Database module configuration.
- `DbSettings` - Database settings.
- `IDbEntity` - Database entity marker.


## ActualChat.Redis

- `RedisModule` - Redis module configuration.
- `RedisMeshLocks` - Redis-based distributed locks.
- `RedisSettings` - Redis settings.
- `RedisSlidingWindowRateLimiter` (sealed class) - Sliding window rate limiter.
- `RedisRateLimitPolicy` (static class) - Builds the API host `RateLimitPolicy`: commands local, the rest via Redis.


## ActualChat.Api.Contracts

- `IAliases` - Service for resolving human-friendly aliases to chats and places.
- `AliasKind` (enum) - Specifies the type of entity an alias points to.
- `AliasTarget` (record) - Represents the target of an alias resolution.
- `IAuthors` - Service for managing chat authors and membership; `Authors_ChangeRole` appoints/removes Owners and Moderators (`Authors_PromoteToOwner` is its obsolete predecessor).
- `IChatMarkupHub` - Provides markup parsing and mention resolution services for a chat.
- `IChatThreads` - Service for managing chat threads (reply threads attached to messages).
- `ThreadStat` (record) - Statistics for a chat thread.
- `IChats` - Service for managing chats, entries, and related operations.
- `IConversations` - Service for managing conversation segments and their summaries.
- `IDiagnostics` - Service for retrieving server mesh diagnostic information.
- `IMentions` - Service for tracking mentions of users in chat messages.
- `IPlaces` - Service for managing places and their members; `ListOwnerIds` / `ListModeratorIds` and `Places_ChangeRole` forward to the place root chat.
- `IReactions` - Service for managing reactions (emoji responses) to chat messages.
- `IRoles` - Service for managing chat roles and permissions; `ListOwnerIds` / `ListModeratorIds` mask anonymous members from non-owner callers.
- `ITranslations` - Service for translating chat messages to different languages.
- `IContacts` - Service for managing user contacts and contact lists.
- `IExternalContactHashes` - Service for tracking external contact sync state via hashes.
- `IExternalContacts` - Service for managing external contacts imported from devices.
- `IInvites` - Service for generating and managing invitation links.
- `ServerKvasClient` - Client-side KVAS implementation that delegates to IServerKvas.
- `IMediaLinkPreviews` - Service for retrieving link preview metadata.
- `IUploads` - Service for managing chunked file uploads.
- `INotifications` - Service for managing user notifications and device registrations.
- `ISearch` - Service for searching contacts and chat entries.
- `IStreamClient` - Client-side interface for accessing audio and transcript streams.
- `ILiveSessions` - Service for live sessions (calls): start/accept/leave, mute peers, `SetHost`. Owners and Moderators can mute participants; Moderators can't mute Owners.
- `IAccounts` - Service for managing user accounts, sessions, and presence.
- `IAvatars` - Service for managing user avatars.
- `RecaptchaValidationResult` (record) - Result of a reCAPTCHA validation request.
- `ICaptcha` - Service for reCAPTCHA token validation.
- `IChatPositions` - Service for tracking user read and view positions in chats.
- `IChatUsages` - Service for tracking recent chat usage patterns.
- `IEmailAuth` - Service for email-based authentication with TOTP codes.
- `IEmails` - Service for sending email communications.
- `IMobileSessions` - Service for mobile app session creation and validation.
- `INativeAuth` - Service for native (iOS/Android) OAuth sign-in flows.
- `IPhoneAuth` - Service for phone-based authentication with TOTP codes.
- `IPhones` - Service for parsing and validating phone numbers.
- `ISystemProperties` - Service for system properties, version checking, and maintenance operations.
- `ServerApiInfo` (record) - Server API version and compatibility information.
- `ITimeZones` - Service for time zone lookup and conversion.
- `IUserPresences` - Service for tracking and querying user online presence.
  

## ActualChat.Api

- `CompatibilityLevel` (enum) - Indicates client-server version compatibility.
- `ContentLinkInfo` (record) - Metadata for a content link including title, picture, and description.
- `Gender` (enum) - User gender options.
- `HostInfoExt` (static class) - Extension methods for HostInfo URL and host resolution.
- `Links` (static class) - Factory methods for building application URLs.
- `LocalAppSettings` (record) - Application settings stored locally on the device.
- `ServiceProviderExt` (static class) - Service provider extension methods for ActualChat services.
- `UrlMapperExt` (static class) - Extension methods for UrlMapper to generate picture preview URLs.
- `ActualOpusStreamConverter` - Converts between ActualChat's native Opus stream format and AudioSource.
- `ActualOpusStreamHeader` (record) - Header for ActualChat's native Opus stream format (A_OPUS_S).
- `AudioCodecKind` (enum) - Specifies the audio codec type.
- `AudioDownloader` (abstract class) - Base class for downloading audio from a URL and converting to AudioSource.
- `AudioFormat` (record) - Describes audio encoding parameters including codec, sample rate, and channel count.
- `AudioFrame` - Represents a single frame of audio data.
- `AudioSettings` (sealed class) - Configuration settings for audio recording and listening behaviors.
- `AudioSource` - Provides a stream of audio frames with format metadata.
- `AudioSourceExt` (static class) - Extension methods for AudioSource concatenation and trimming.
- `AudioStreamConverterExt` (static class) - Extension methods for IAudioStreamConverter.
- `HttpClientAudioDownloader` - Downloads audio via HTTP using IHttpClientFactory.
- `IAudioStreamConverter` - Converts between byte streams and AudioSource.
- `OggHeader` (struct) - Represents an Ogg page header.
- `OggHeaderType` (enum) - Ogg page header type flags.
- `OggOpusStreamConverter` - Converts AudioSource to Ogg/Opus stream format.
- `OggOpusWriter` - Writes Ogg/Opus format audio streams.
- `OpusHead` (struct) - Opus identification header for Ogg/Opus streams.
- `OpusTags` (struct) - Opus comment header containing vendor and user metadata.
- `RecordingStreamExt` (static class) - Extension methods for converting byte streams to recording parts.
- `WebMStreamConverter` (sealed class) - Converts between WebM container format and AudioSource.
- `EbmlDataFormatException` - Exception thrown when EBML data format is invalid or corrupt.
- `EbmlElement` - Represents a parsed EBML element with identifier, size, and type descriptor.
- `EbmlElementDescriptor` - Describes an EBML element type including its identifier and data type.
- `EbmlElementType` (enum) - Specifies the data type of an EBML element.
- `EbmlHelper` (static class) - Helper methods for EBML (Extensible Binary Meta Language) element encoding.
- `Lacing` (enum) - Specifies the lacing mode for WebM/Matroska blocks.
- `MatroskaElementDescriptorAttribute` (attribute) - Maps a property or class to a Matroska element identifier.
- `WebMReadResultKind` (enum) - Specifies the type of element returned by WebMReader.
- `WebMReader` - Reads and parses WebM container format data.
- `WebMWriter` - Writes data in WebM container format.
- `WebMDocument` (record) - Represents a complete WebM document with header, segment, and clusters.
- `WebMDocumentBuilder` (sealed class) - Builder for creating WebMDocument instances.
- `BaseModel` (abstract class) - Base class for WebM/Matroska model elements.
- `BlockAdditional` (sealed class) - Represents additional block data in a Matroska BlockGroup.
- `BlockVirtual` (sealed class) - Represents a virtual block (deprecated Matroska element).
- `EbmlEntryType` (enum) - Specifies the type of top-level EBML entry.
- `EncryptedBlock` (sealed class) - Represents an encrypted block in a Matroska file.
- `IParseRawBinary` - Interface for models that can parse raw binary data.
- `RootEntry` (abstract class) - Base class for top-level WebM elements (EBML, Segment, Cluster).
- `SimpleTag` (sealed class) - Represents a simple metadata tag in a Matroska file.
- `TrackType` (enum) - Specifies the type of track in a WebM file.
- `Author` (record) - Represents a chat participant with an avatar identity.
- `AuthorFull` (record) - Extended author information with full profile details.
- `AuthorRules` (record) - Permission rules for an author in a chat; `IsOwner()` / `CanModerate()` / `CanRead()` and friends test `Permissions`.
- `CachingMarkupParser` - Caching decorator for IMarkupParser using LRU cache.
- `Chat` (record) - Represents a chat with metadata, rules, and settings.
- `ChatEntry` (abstract record) - Base class for chat entries (messages, system events).
- `ChatPermissions` (enum) - Permission flags for chat operations; `Moderate` implies `EditProperties` + `EditMembers`, and `Owner` implies `Moderate`.
- `CodeBlockMarkup` (sealed class) - Represents a fenced code block with optional language.
- `Conversation` (record) - Represents a conversation segment with AI-generated summary.
- `IMarkupParser` - Parses text into markup elements.
- `IMentionResolver` - Two interfaces in this file: (1) `IMentionResolver<T>` resolves a single MentionMarkup to entity T; (2) non-generic `IMentionResolver` is a markup-tree rewriter (formerly IMentionNamer).
- `ListItemMarkup` (sealed class) - Represents a single item in a ListMarkup.
- `ListMarkup` (sealed class) - Represents an unordered list of items in markup.
- `Markup` (abstract class) - Base class for chat message markup elements.
- `MarkupConsumer` (enum) - Defines the context where markup text is being displayed.
- `MarkupConsumerExt` (static class) - Extension methods for MarkupConsumer to get display limits.
- `MarkupExt` (static class) - Extension methods for Markup traversal and inspection.
- `MarkupParser` - Parses chat message text into Markup elements.
- `MarkupSeq` (sealed class) - Represents a sequence of markup elements.
- `Mention` (record) - Represents a mention of a user in a chat entry.
- `MentionMarkup` (class) - Base mention element; subclasses below carry pre-resolved cached data.
- `AuthorMention` (sealed class) - MentionMarkup with cached Author for an `a:` mention (legacy).
- `UserMention` (sealed class) - MentionMarkup with cached Account + IsChatMember for a `u:` mention.
- `ChatMention` (sealed class) - MentionMarkup with cached Chat for a `c:` mention.
- `PlaceMention` (sealed class) - MentionMarkup with cached Place for a `p:` mention.
- `EmojiMention` (sealed class) - MentionMarkup with cached Glyph / CustomPicture for an `e:` mention; renders large when it's the whole message.
- `MentionCandidate` (record) - Unified picker candidate (User/Chat/Emoji) returned by LocalSearchUI.
- `MentionCandidateFilters` (static class) - `Func<MentionCandidate, bool>` category filters (All/User/Chat/Emoji) + `KindRank` ordering + `FilterAndRank(filter, query, limit, recencyScores?)` (empty query → recents first; typed → additive recency boost), keyed off `MentionRef.Kind`.
- `MentionResolver` (record : AsyncMarkupRewriter) - Tree rewriter; delegates per-mention enrichment to IChatMentionResolver.Enrich (was MentionNamer).
- `NewLineMarkup` (sealed class) - Represents a line break in markup.
- `Place` (record) - Represents a place (community container for chats).
- `PlaceRules` (record) - Permission rules for an author in a place; the place-level counterpart of `AuthorRules`.
- `PlacePermissions` (enum) - Permission flags for place operations; bit values must stay aligned with `ChatPermissions` - `Places.ToPlaceRules` casts between them.
- `PlainTextMarkup` (sealed class) - Represents plain text content without formatting.
- `PlayableTextMarkup` (sealed class) - Represents text in playable audio message markup.
- `PreformattedTextMarkup` (sealed class) - Represents inline monospace/code text wrapped in backticks.
- `Reaction` (record) - Represents an emoji reaction on a chat entry.
- `ReactionSummary` (record) - Summary of reactions on a chat entry.
- `Role` (record) - Represents a chat role with permissions; `Fix()` clamps system roles to their canonical permission set.
- `StylizedMarkup` (sealed class) - Represents styled text content (bold, italic).
- `TextEntry` (record) - Represents a text message entry in a chat.
- `TextEntryAttachment` (record) - Attachment on a text entry.
- `ChatEntryHashExt` (static class) - Extension methods for chat entry hashing.
- `TextMarkup` (abstract class) - Base class for text-based markup elements.
- `TextMarkupKind` (enum) - Defines the type of text markup content.
- `TextStyle` (enum) - Defines text styling options.
- `Translation` (record) - Represents a translated version of chat entry content.
- `UnparsedTextMarkup` (sealed class) - Represents text that should not be parsed for markup.
- `UrlMarkup` (sealed class) - Represents a URL link in markup.
- `UrlMarkupKind` (enum) - Defines the type of URL in markup.
- `Contact` (record) - Represents a contact (chat membership or external contact).
- `ExternalContact` (record) - Represents a contact imported from the device's address book.
- `ExternalContactFull` (record) - Extended external contact with full name components and contact info hashes.
- `ExternalContactExt` (static class) - Extension methods for modifying ExternalContactFull instances.
- `ExternalContactHasher` (sealed class) - Computes SHA256 hashes for external contacts to detect changes.
- `ExternalContactsHash` (record) - Hash of external contacts for sync detection.
- `Invite` (record) - Represents an invitation to join a chat or place.
- `InviteChatLinkPreview` (record) - Preview data for an invite link showing the target chat or place.
- `LiveStreamSettings` (sealed class) - Configures audio playback behavior during live streaming.
- `GrabStatus` (enum) - Specifies the status of a link preview grab/crawl operation.
- `IMediaSource` - Provides access to a stream of media frames with format metadata.
- `IMediaStreamPart` - Represents a part of a media stream (either format metadata or a frame).
- `LinkPreview` (record) - Metadata for a webpage including title, description, and image.
- `LinkPreviewMode` (enum) - Specifies the display mode for link previews.
- `Media` (record) - Represents a media file with metadata and content reference.
- `MediaExt` (static class) - Extension methods for Media.
- `MediaFormat` (abstract record) - Base class for media format descriptors (audio, video).
- `MediaFrame` - Represents a frame of media data with timing information.
- `MediaSource` (abstract class) - Base class providing a memoized stream of media frames with format metadata.
- `Picture` (record) - Represents a picture with multiple size variants.
- `RecordingPart` - Represents a part of a recording stream (data, pause, or resume event).
- `Upload` (record) - Represents a file upload session with progress tracking.
- `UploadExt` (static class) - Extension methods for Upload.
- `ActivePlaybackInfo` - Tracks currently playing tracks and their playback states.
- `ITrackPlayerFactory` - Creates TrackPlayer instances for media sources.
- `IPlaybackCommand` - Marker interface for playback control commands.
- `IPlaybackFactory` - Creates Playback instances. Must be a scoped service.
- `IPlayerCommand` - Marker interface for track player control commands.
- `Playback` - Manages audio playback for a session.
- `PlaybackFactory` - Default implementation of IPlaybackFactory.
- `PlayerState` (record) - Represents the current state of a TrackPlayer.
- `PlayerStateChangedEventArgs` (record) - Event arguments for player state changes.
- `TrackInfo` (record) - Metadata about a media track being played.
- `TrackPlayer` (abstract class) - Base class for playing audio tracks from a media source.
- `DeviceType` (enum) - Specifies the type of push notification device.
- `Notification` (record) - Represents a push notification to be sent to a user device.
- `ContactSearchQuery` (record) - Query parameters for searching contacts.
- `ContactIdExt` (static class) - Extension methods for ContactId.
- `ContactSearchResult` - Represents a contact match from a search query.
- `ContactSearchResultPage` - A paginated collection of contact search results.
- `EntrySearchQuery` (record) - Query parameters for searching chat entries.
- `EntrySearchResult` - Represents a chat entry match from a search query.
- `EntrySearchResultPage` - A paginated collection of chat entry search results.
- `SearchScope` (enum) - Defines the type of entities to search for.
- `LinearMapDtwRemapper` - Remaps transcript diffs using dynamic time warping (DTW) alignment.
- `Transcript` (record) - Represents a transcript of audio including text segments with timing.
- `TranscriptDiff` (record) - Represents changes to a transcript (added, updated, removed segments).
- `TranscriptException` - Exception thrown for invalid transcript operations.
- `TranscriptionEngine` (enum) - Enumerates available speech-to-text transcription engines.
- `TranscriptionOptions` (record) - Configures transcription settings including language and detection.
- `Account` (record) - Represents a user account with associated avatar and status.
- `AccountExt` (static class) - Extension methods for Account.
- `AuthorId` (struct) - Unique identifier for a chat author (participant).
- `AudioEntryId` (struct) - Unique identifier for an audio entry.
- `ChatEntryId` (struct) - Unique identifier for a chat entry.
- `ChatEntryKind` (enum) - Specifies the type of chat entry (text, audio, system).
- `ChatId` (struct) - Unique identifier for a chat.
- `ChatKind` (enum) - Specifies the type of chat (group, peer, place).
- `GroupChatId` (struct) - Identifier for a group chat.
- `LocalChatId` (struct) - Local identifier for a chat.
- `PeerChatId` (struct) - Identifier for a peer-to-peer chat.
- `PlaceChatId` (struct) - Identifier for a place chat.
- `ThreadChatId` (struct) - Identifier for a thread chat.
- `ContactId` (struct) - Unique identifier for a contact.
- `ContactKind` (enum) - Specifies the type of contact (user, group, place).
- `ContactSubset` (enum) - Specifies which subset of contacts to query.
- `ContentId` (struct) - Unique identifier for content.
- `ConversationId` (struct) - Unique identifier for a conversation segment.
- `ExplicitNotificationId` (struct) - Unique identifier for an explicit notification.
- `ExplicitNotificationKind` (enum) - Specifies the type of explicit notification.
- `ExternalContactId` (struct) - Unique identifier for an external contact.
- `MediaId` (struct) - Unique identifier for a media item.
- `MentionRef` (class) - `<prefix>:<localId>` reference to a mention target; dispatches via MentionKind to `.Target` (an `IMentionTarget`).
- `MentionKind` (sealed class) - Registered mention prefix (`a`/`u`/`c`/`p`/`e`) with parse fn (`a` author = legacy/anonymous-only).
- `IMentionTarget` (interface) - Marker for identifier types usable as MentionRef targets (UserId/AuthorId/ChatId/PlaceId/EmojiRef).
- `EmojiRef` (class) - URL-encoded emoji slug or glyph reference; IMentionTarget for `e:` (`EmojiRef.FromText` encodes raw text).
- `NotificationId` (struct) - Unique identifier for a notification.
- `NotificationKind` (enum) - Specifies the type of notification.
- `PlaceId` (struct) - Unique identifier for a place.
- `PrincipalId` (struct) - Unique identifier for a principal (user, role, or system).
- `PrincipalKind` (enum) - Specifies the type of principal.
- `RoleId` (struct) - Unique identifier for a role.
- `StreamId` (struct) - Unique identifier for a stream.
- `TextEntryId` (struct) - Unique identifier for a text entry.
- `TranslationId` (struct) - Unique identifier for a translation.
- `TranslationSourceId` (struct) - Unique identifier for a translation source.
- `UploadId` (struct) - Unique identifier for an upload.
- `UserId` (struct) - Unique identifier for a user.
- `AccountFull` (record) - Extended account information including full details.
- `AccountFullExt` (static class) - Extension methods for AccountFull.
- `AccountStatus` (enum) - Specifies the status of a user account.
- `Avatar` (record) - Represents a user's avatar with name, picture, and bio information.
- `AvatarFull` (record) - Extended avatar information with all profile details.
- `ChatNotificationMode` (enum) - Specifies the notification preference for a chat.
- `ChatPosition` - Represents a user's read or view position in a chat.
- `ChatPositionKind` (enum) - Specifies the type of position tracked in a chat.
- `ChatUsageListKind` (enum) - Specifies the type of chat usage tracking list.
- `ListeningMode` (enum) - Specifies the duration for listening to a chat.
- `ListeningModeInfo` (sealed class) - Provides metadata for a ListeningMode including duration and display text.
- `Presence` (enum) - Specifies a user's online presence status.
- `TimeZone` (record) - Represents a time zone with identifier and display name.
- `TotpPurpose` (enum) - Specifies the purpose of a time-based one-time password (TOTP).
- `UserAppSettings` (record) - Application-level user preferences.
- `UserAvatarSettings` (record) - Avatar and profile customization settings.
- `UserBubblesSettings` (record) - Onboarding bubble tooltip configuration per user.
- `UserChatSettings` (record) - Per-chat user preferences and state.
- `UserEmailsSettings` (record) - Email notification and digest preferences.
- `UserLanguageSettings` (record) - Language and localization preferences.
- `UserListeningSettings` (record) - Audio listening mode and behavior preferences.
- `UserNavbarSettings` (record) - Navigation bar customization settings.
- `UserOnboardingSettings` (record) - Onboarding state and completion tracking.
- `UserChatRecordingDetectedLanguage` (record) - Detected language for chat recording.
- `UserChatSettingsExt` (static class) - Extension methods for UserChatSettings.
- `UserDeviceId` (struct) - Unique identifier for a user device.
- `UserIdentityExt` (static class) - Extension methods for user identity.
- `UserTranscriptionEngineSettings` (record) - Transcription engine selection and configuration.
- `VoiceMode` (enum) - Specifies whether messages include voice, text, or both.
- `ChangeExt` (static class) - Extension methods for Change.
- `DiffHandlerBase<T>` (abstract class) - Base class for diff handlers.
- `MissingDiffHandler<T>` - Handler for missing diffs.
- `NullableDiffHandler<T>` - Handler for nullable diffs.
- `ObjectDiffHandler<T>` - Handler for object diffs.
- `OptionDiffHandler<T>` - Handler for option diffs.
- `RecordDiffHandler<T>` - Handler for record diffs.
- `SetDiff<T>` (record) - Represents a set diff.
- `SetDiffHandler<T>` - Handler for set diffs.
- `StringDiff` (record) - Represents a string diff.
- `StringDiffHandler` - Handler for string diffs.
- `IDiffHandler<T>` - Interface for diff handlers.
- `ThreadContact` (record) - Contact information for a thread.


## ActualChat.Chat.Contracts

- `ChangedAuthorsQuery` (record) - Query parameters for listing changed authors by version range.
- `ChangedChatsQuery` (record) - Query parameters for listing changed chats by version range.
- `ChangedEntriesQuery` (record) - Query parameters for listing changed chat entries by version range.
- `IAliasBackend` - Backend service for managing chat and place aliases.
- `IAuthorsBackend` - Backend service for managing chat authors (participants).
- `IAuthorsUpgradeBackend` - Backend service for author migration and upgrade operations.
- `IBackendChatMarkupHub` - Backend variant of IChatMarkupHub for server-side markup parsing.
- `IChatEntryLanguagesBackend` - Backend service for detecting and storing chat entry languages.
- `IChatThreadsBackend` - Backend service for managing chat threads (reply chains).
- `IChatsBackend` - Backend service for chat operations including entries, tiles, and chat management.
- `IChatsUpgradeBackend` - Backend service for chat migration and upgrade operations.
- `IContentLinksBackend` - Backend service for resolving content links to their metadata.
- `IConversationsBackend` - Backend service for managing conversations with AI summaries.
- `IDiagnosticsBackend` - Backend service for system diagnostics and mesh health information.
- `IMentionsBackend` - Backend service for tracking mentions in chat entries.
- `IPlacesBackend` - Backend service for managing places (organizational containers for chats).
- `IReactionsBackend` - Backend service for managing reactions on chat entries.
- `IRolesBackend` - Backend service for managing chat roles and permissions; unlike `IRoles` it never masks anonymous members, so owner-immunity checks must go through it.
- `ITranslationsBackend` - Backend service for translating chat entry content between languages.
- `ReadPositionsStatBackend` (record) - Tracks the top read positions for users in a chat.
- `RequestedAuthorKind` (enum) - Specifies the level of detail to return for author queries.
- `AuthorsBackendExt` (static class) - Extension methods for IAuthorsBackend.
- `ChatsBackendExt` (static class) - Extension methods for IChatsBackend.
- `PlacesBackendExt` (static class) - Extension methods for IPlacesBackend.
- `RolesBackendExt` (static class) - Extension methods for IRolesBackend.


## ActualChat.Users.Contracts

- `IAccountsBackend` - Backend service for managing user accounts.
- `IAvatarsBackend` - Backend service for managing user avatars.
- `IChatPositionsBackend` - Backend service for tracking user chat positions.
- `IChatUsagesBackend` - Backend service for tracking chat usage statistics.
- `IEmailsBackend` - Backend service for email operations.
- `ISessionsBackend` - Backend service for managing user sessions.
- `ISessionTemporalsBackend` - Backend service for transient/temporal session data.
- `SessionTemporalsBackend` - Implementation of ISessionTemporalsBackend.
- `IUserPresencesBackend` - Backend service for user presence tracking.
- `IServerKvasBackend` - Backend service for server-side key-value store.
- `UserScopedKvasBackend` - User-scoped wrapper around IServerKvasBackend.
- `ServerKvasBackendExt` (static class) - Extension methods for IServerKvasBackend.
- `AccountsBackendExt` (static class) - Extension methods for IAccountsBackend.


## ActualChat.Contacts.Contracts

- `ChangedContactsQuery` (record) - Query parameters for listing changed contacts by version range.
- `IContactsBackend` - Backend service for managing user contacts and memberships.
- `IExternalContactHashesBackend` - Backend service for managing external contact hash checksums.
- `IExternalContactsBackend` - Backend service for managing external contacts synced from devices.
- `ContactsBackendExt` (static class) - Extension methods for IContactsBackend.


## ActualChat.Invite.Contracts

- `IInvitesBackend` - Backend service for managing invitation links.


## ActualChat.Media.Contracts

- `IGrabStatusesBackend` - Backend service for tracking link preview grab statuses.
- `ILinkPreviewsBackend` - Backend service for link preview generation and caching.
- `IMediaBackend` - Backend service for media management.
- `IMediaProgressBackend` - Backend service for tracking media processing progress.
- `IUploadsBackend` - Backend service for file upload handling.
- `GrabStatusesBackendExt` (static class) - Extension methods for IGrabStatusesBackend.


## ActualChat.Notifications.Contracts

- `Device` (record) - Represents a user's push notification device registration.
- `ExplicitNotification` (record) - Represents an explicit notification to be sent.
- `INotificationsBackend` - Backend service for push notification management.


## ActualChat.Streaming.Contracts

- `AudioRecord` (record) - Represents a recorded audio segment with metadata.
- `ILiveBackend` - Backend service for live audio streaming.
- `ILiveSessionsBackend` - Backend service for live-session (call) state: membership, host assignment, `MuteAll` / `SetHost`.
- `ILiveAudioBackend` - Backend service for live audio sessions.
- `ILiveVideoBackend` - Backend service for live video sessions.
- `IVideoStreamingBackend` - Backend service for video streaming operations.
- `IStreamingBackend` - Backend service for audio streaming operations.
- `ITranscriber` - Interface for audio transcription.
- `ITranscriberFactory` - Factory for creating transcriber instances.


## ActualChat.Search.Contracts

- `ISearchBackend` - Backend service for full-text search operations.


## ActualChat.MLSearch.Contracts

(Marker contract project — types are defined in `ActualChat.Search.Contracts` and `ActualChat.MLSearch.Service`.)


## ActualChat.Transcription.Contracts

(Marker contract project — transcription contracts are defined alongside `ActualChat.Streaming.Contracts`.)


## ActualChat.Asr

- `ParakeetModel` - NVIDIA Parakeet ASR model wrapper.
- `ParakeetModelDownloader` - Downloads Parakeet model files.
- `ProgressiveStreamingHandler` - Streams ASR results progressively.
- `TdtDecoder` - Token-and-Duration Transducer decoder.
- `TranscriptionResult` - Result of ASR transcription.
- `Vocabulary` - ASR model vocabulary.


## ActualChat.Users.Service

- `Accounts` - Implementation of IAccounts for user account management.
- `AccountsBackend` - Implementation of IAccountsBackend.
- `Avatars` - Implementation of IAvatars for user avatar management.
- `AvatarsBackend` - Implementation of IAvatarsBackend.
- `ChatPositions` - Implementation of IChatPositions for tracking chat positions.
- `ChatPositionsBackend` - Implementation of IChatPositionsBackend.
- `ChatUsages` - Implementation of IChatUsages for tracking chat usage.
- `ChatUsagesBackend` - Implementation of IChatUsagesBackend.
- `Emails` - Implementation of IEmails for email operations.
- `EmailsBackend` - Implementation of IEmailsBackend.
- `ServerKvas` - Implementation of IServerKvas for server-side key-value store.
- `ServerKvasBackend` - Implementation of IServerKvasBackend.
- `SessionsBackend` - Implementation of ISessionsBackend.
- `UserPresences` - Implementation of IUserPresences for presence tracking.
- `UserPresencesBackend` - Implementation of IUserPresencesBackend.


## ActualChat.Chat.Service

- `Aliases` - Implementation of IAliases for alias resolution.
- `AliasBackend` - Implementation of IAliasBackend.
- `Authors` - Implementation of IAuthors for chat author management.
- `AuthorsBackend` - Implementation of IAuthorsBackend.
- `BackendChatMarkupHub` - Implementation of IBackendChatMarkupHub.
- `ChatMarkupHub` - Implementation of IChatMarkupHub.
- `Chats` - Implementation of IChats for chat management.
- `ChatsBackend` - Implementation of IChatsBackend.
- `ChatThreads` - Implementation of IChatThreads.
- `ChatThreadsBackend` - Implementation of IChatThreadsBackend.
- `Conversations` - Implementation of IConversations.
- `ConversationsBackend` - Implementation of IConversationsBackend.
- `Diagnostics` - Implementation of IDiagnostics.
- `DiagnosticsBackend` - Implementation of IDiagnosticsBackend.
- `Mentions` - Implementation of IMentions.
- `MentionsBackend` - Implementation of IMentionsBackend.
- `Places` - Implementation of IPlaces for place management.
- `PlacesBackend` - Implementation of IPlacesBackend.
- `Reactions` - Implementation of IReactions for message reactions.
- `ReactionsBackend` - Implementation of IReactionsBackend.
- `Roles` - Implementation of IRoles for role management.
- `RolesBackend` - Implementation of IRolesBackend.
- `Translations` - Implementation of ITranslations.
- `TranslationsBackend` - Implementation of ITranslationsBackend.


## ActualChat.Contacts.Service

- `Contacts` - Implementation of IContacts for contact management.
- `ContactsBackend` - Implementation of IContactsBackend.
- `ExternalContactHashes` - Implementation of IExternalContactHashes.
- `ExternalContactHashesBackend` - Implementation of IExternalContactHashesBackend.
- `ExternalContacts` - Implementation of IExternalContacts.
- `ExternalContactsBackend` - Implementation of IExternalContactsBackend.


## ActualChat.Invite.Service

- `Invites` - Implementation of IInvites for invitation management.
- `InvitesBackend` - Implementation of IInvitesBackend.
- `LegacyInvites` - Legacy invite handling for backward compatibility.
- `DbInvite` - Database entity for invites.
- `InviteDbContext` - EF Core context for invites.
- `InviteDbInitializer` - Database initializer for invites.
- `InviteServiceModule` - DI module for Invite service.


## ActualChat.Media.Service

- `MediaBackend` - Implementation of IMediaBackend.
- `MediaService` - Service for media operations.
- `MediaSaver` - Saves processed media files.
- `MediaUploader` - Handles file uploads for media.
- `MediaLinkPreviews` - Implementation of IMediaLinkPreviews.
- `MediaProgressBackend` - Implementation of IMediaProgressBackend.
- `Uploads` - Implementation of IUploads for file upload handling.
- `UploadsBackend` - Implementation of IUploadsBackend.
- `UploadsStorage` - Storage layer for uploaded files.
- `LinkPreviewsBackend` - Implementation of ILinkPreviewsBackend.
- `GrabStatusesBackend` - Implementation of IGrabStatusesBackend.
- `ContentController` - API controller for media content retrieval.
- `Crawler` - Web page crawler for metadata.
- `EgressGuard` - Guards against egress to forbidden domains.
- `Gifs` - Animated GIF handling.
- `HostWildcard` - Wildcard hostname matcher.
- `ICrawlingHandler` - Interface for crawling handlers.
- `ImageGrabber` - Grabs images from URLs.
- `ImageLinkHandler` - Handler for image links.
- `OpenGraphParser` - Parses Open Graph metadata.
- `Resource` - Resource loader for media service.
- `RobotsFiles` - Robots.txt file handler.
- `SpecialAddresses` - Special address handling.
- `WebSiteHandler` - Handler for general websites.
- `LinkPreviewFlow` - Flow for generating link previews.
- `PreviewThumbnailUpdateFlow` - Flow for updating preview thumbnails.
- `UploadProcessingFlow` - Flow for processing uploads.
- `MediaSettings` - Media service settings.
- `MetadataSerializer` - Serializer for media metadata.
- `DbGrabStatus` - Database entity for grab status.
- `DbLinkPreview` - Database entity for link previews.
- `DbMedia` - Database entity for media.
- `DbMediaProgress` - Database entity for media processing progress.
- `MediaDbContext` - EF Core context for media.
- `MediaDbInitializer` - Database initializer for media.
- `MediaServiceModule` - DI module for Media service.


## ActualChat.Notifications.Service

- `Notifications` - Implementation of INotifications for push notifications.
- `NotificationsBackend` - Implementation of INotificationsBackend.
- `NotificationFlow` - Flow for sending notifications.
- `NotificationHelper` - Helper utilities for notifications.
- `FirebaseMessagingClient` - Firebase Cloud Messaging client.
- `DbDevice` - Database entity for notification devices.
- `DbExplicitNotification` - Database entity for explicit notifications.
- `DbNotification` - Database entity for notifications.
- `NotificationDbContext` - EF Core context for notifications.
- `NotificationDbInitializer` - Database initializer for notifications.
- `NotificationServiceModule` - DI module for Notification service.


## ActualChat.Streaming.Service

- `FlowBackend` - Implementation of IFlowBackend.
- `LiveBackend` - Implementation of ILiveBackend.
- `LiveSessions` - Implementation of ILiveSessions; `GetCallAuthority` decides who may mute whom (Owners immune to Moderators) and who may reassign the host.
- `LiveSessionsBackend` - Implementation of ILiveSessionsBackend.
- `LiveAudioBackend` - Implementation of ILiveAudioBackend.
- `LiveVideoBackend` - Implementation of ILiveVideoBackend.
- `StreamingBackend` - Implementation of IStreamingBackend.
- `VideoStreamingBackend` - Implementation of IVideoStreamingBackend.
- `TranscriberFactory` - Implementation of ITranscriberFactory.


## ActualChat.Search.Service

- `Search` - Implementation of ISearch.
- `SearchBackend` - Implementation of ISearchBackend.


## ActualChat.MLSearch.Service

- `MLSearchServiceModule` - DI module for ML search service.
- `MLSearchSettings` - ML search configuration.
- `OpenSearchSettings` - OpenSearch-specific settings.
- `MLSearchDbContext` - EF Core context for ML search.
- `MLSearchDbInitializer` - Database initializer for ML search.
- `MLSearchInstruments` - Diagnostics instruments for ML search.
- `OpenSearchConfigurator` - Configures OpenSearch client.
- `OpenSearchTypeInfoResolver` - Custom type info resolver for OpenSearch.
- `HighlightsConverter` - OpenSearch highlights converter.
- `Search` - Implementation of ISearch using ML search.
- `SearchBackend` - Implementation of ISearchBackend.
- `IIndexedUserMinimalUpsert` - Minimal user upsert interface.
- `IIndexedUserUpsertForPlacesOnly` - Place-only user upsert interface.
- `IIndexedUserUpsertWithoutPlaces` - User upsert without places interface.
- `IndexedUserContact` - Indexed contact document.
- `AccountIndexingFlow` - Flow for indexing accounts.
- `EntryIndexingFlow` - Flow for indexing entries.
- `EntryIndexingMasterFlow` - Master flow for entry indexing.
- `GroupIndexingFlow` - Flow for indexing entry groups.
- `PlaceContactIndexingFlow` - Flow for indexing place contacts.
- `PlaceIndexingFlow` - Flow for indexing places.
- `UserContactIndexingFlow` - Flow for indexing user contacts.
- `AccountExt` (static class) - Account extensions for search.
- `ChatExt` (static class) - Chat extensions for search.
- `ChatEntryExt` (static class) - Chat entry extensions for search.
- `ContactExt` (static class) - Contact extensions for search.
- `ContactSearchQueryExt` (static class) - Contact search query extensions.
- `EntrySearchQueryExt` (static class) - Entry search query extensions.
- `PlaceExt` (static class) - Place extensions for search.
- `UriAttribute` (attribute) - URI validation attribute.


## ActualChat.Flows.Service

- `FlowBackend` - Backend implementation for flows.
- `FlowsServiceModule` - DI module for Flows service.
- `DbFlow` - Database entity for flows.
- `FlowsDbContext` - EF Core context for flows.
- `FlowsDbInitializer` - Database initializer for flows.


## ActualChat.Chat.ML

- `IChatDigestSummarizer` - Interface for chat digest summarization.
- `ChatDigestSummarizer` - Summarizes chat digests with AI.
- `ChatDigestSummarizerStub` - Stub implementation for chat digest summarization.
- `IConversationSummarizer` - Interface for conversation summarization.
- `ConversationSummarizer` - Summarizes conversations with AI.
- `ConversationSummarizerStub` - Stub implementation for conversation summarization.
- `IChatDialogFormatter` - Interface for chat dialog formatting.
- `ChatDialogFormatterExt` (static class) - Extensions for chat dialog formatting.
- `IThreadInsightExtractor` - Interface for thread insight extraction.
- `ThreadInsightExtractor` - Extracts insights from thread messages.
- `IEntryGroupExtractor` - Interface for entry group extraction.
- `EntryGroupExtractor` - Extracts groups from chat entries.
- `EntryGroupBuilder` - Builds groups of chat entries.
- `EntryGroupLimit` (enum) - Limits for entry grouping.
- `IEmbeddingsCalculator` - Interface for embeddings calculation.
- `EmbeddingsCalculator` - Calculates embeddings for text.
- `EmbeddingSettings` - Configuration for embeddings.
- `RateLimitedChatCompletionService` - Rate-limited chat completion wrapper.
- `ChatCompletionServiceExt` (static class) - Extensions for chat completion service.
- `OpenAITranscriber` - Transcriber using OpenAI API.
- `TokenEstimator` - Estimates token counts for prompts.


## ActualChat.UI

- `AppRemoteComputedCache` (abstract class) - Client-side computed value cache.
- `KvasarRemoteComputedCache` - Kvasar-backed remote computed cache used by the MAUI app.
- `ChunkedFileUploader` - Resumable file uploads with retry.
- `SystemSettingsUI` - System settings UI.
- `UICoreModule` - UI core module.


## ActualChat.UI.Blazor

- `AccountUI` - Account state and authentication flow.
- `BubbleUI` - Onboarding bubble tooltips.
- `ComponentBase<THub>` (abstract class) - Blazor component base with service shortcuts.
- `ComputedStateComponent<TState, THub>` (abstract class) - Component with Fusion computed state.
- `ContactsPermissionHandler` - Contacts permission handling.
- `DebugUI` - Debugging utilities.
- `DeviceAwakeUI` - Device wake state tracking.
- `History` - Browser navigation history management.
- `HistoryItem` (record) - Single entry in browser history.
- `KeepAwakeUI` - Prevents screen sleep.
- `LogUI` - Application log viewer.
- `Menu<T>` - Menu component.
- `MicrophonePermissionHandler` - Microphone permission handling.
- `ModalRef` - Modal dialog reference.
- `ModalUI` - Modal dialog management.
- `NavbarUI` - Navbar management.
- `PanelsUI` - Panel management.
- `PermissionHandler` (abstract class) - Permission request handling base.
- `ReconnectUI` - RPC connection state monitoring.
- `ThemeUI` - Theme management.
- `ToastUI` - Toast notification management.
- `TotpUI` - TOTP code handling.
- `TuneUI` - Haptic feedback and sounds.
- `UIHub` - Central hub providing access to all UI services.
- `UIServiceBase<THub>` (abstract class) - Base for UI services.
- `UIWorkerBase<THub>` (abstract class) - Base for UI background workers.
- `UserActivityUI` - User activity tracking.
- `VirtualList<T>` - Virtualized list with windowed rendering.
- `WebRemoteComputedCache` - IndexedDB-based remote computed cache.


## ActualChat.UI.Blazor.App

- `AppUIHub` - Extended UI hub with chat-specific services.
- `AudioRecorder` - Audio recording component.
- `ChatAudioUI` - Audio listening/recording state management.
- `ChatList` - Chat list component.
- `ChatListUI` - Chat list filtering and sorting.
- `ChatPlayer` (abstract class) - Base class for playing audio entries.
- `ChatPlayers` - Orchestrates audio playback across chats.
- `ChatUI` - Chat selection, read positions, and chat state.
- `ChatView` - Main chat view component.
- `EditMembersUI` - Member editing utilities.
- `LanguageUI` - Language preferences.
- `LiveStreamUI` - Live streaming management.
- `MarkupEditor` - Markup editing component.
- `MarkupView` (abstract class) - Markup rendering component.
- `OnboardingUI` - User onboarding flow.
- `RecorderStateHub` - Recording state management.
- `SearchUI` - Unified search across chats.
- `SendingMessages` - Message sending with retry logic.


## ActualChat.UI.App

- `AppServerInstanceSelector` - Selects which app server instance to connect to.
- `IncomingShareSuggestions` - Handles OS-level incoming share suggestions.
- `VideoTranscoder` - Transcodes video files for upload/playback.


## ActualChat.UI.Blazor.AppPack

- `WebApp` - Web app entry point and packaging glue (used by ILRepack).


## ActualChat.Mjml.Blazor

Blazor components for building MJML email templates. Each MJML element has a corresponding component, plus enum types and `*Ext` helpers for property values.

- `Mjml`, `MjmlAccordion`, `MjmlAccordionElement`, `MjmlAccordionText`, `MjmlAccordionTitle`, `MjmlAll`, `MjmlAttributes`, `MjmlBody`, `MjmlBreakpoint`, `MjmlButton`, `MjmlCarousel`, `MjmlCarouselImage`, `MjmlClass`, `MjmlColumn`, `MjmlDivider`, `MjmlFont`, `MjmlGroup`, `MjmlHead`, `MjmlHero`, `MjmlHtmlAttribute`, `MjmlHtmlAttributes`, `MjmlImage`, `MjmlInclude`, `MjmlNavbar`, `MjmlNavbarLink`, `MjmlPreview`, `MjmlRaw`, `MjmlSection`, `MjmlSelector`, `MjmlSocial`, `MjmlSocialElement`, `MjmlSpacer`, `MjmlStyle`, `MjmlTable`, `MjmlText`, `MjmlTitle`, `MjmlWrapper` — Blazor components for MJML email template building.
- Enum/extension pairs (`MjmlButtonAlign`, `MjmlSectionDirection`, `MjmlSocialMode`, `MjmlStyleInline`, etc.) typed property values for the components above.


## ActualChat.Users.Templates

- `BlazorRenderer` - Renders user-facing email templates with Blazor.
- `DigestArgs` - Arguments for the digest email template.


## ActualChat.Kubernetes

- `KubernetesModule` - DI module for Kubernetes integration.
- `KubernetesSettings` - Kubernetes integration settings.
- `IKubeInfo` - Interface for cluster info.
- `KubeInfo` - Kubernetes cluster information accessor.
- `KubeLeaseClient` - Client for Kubernetes leader-election leases.
- `KubeMeshLocks` - Kubernetes-based distributed locks via leases.
- `KubeServices` - Kubernetes service discovery.
- `KubeToken` - Kubernetes auth token holder.
- `EndpointDiscoveryWorker` - Worker that discovers service endpoints.
- `Annotations` (static class) - Kubernetes annotation key constants.
- `Labels` (static class) - Kubernetes label key constants.
- `ChangeType` (enum) - Types of Kubernetes resource changes.
- `ServiceProtocol` (enum) - Kubernetes service protocol types.
- `KubeServiceProtocol` (enum) - Service protocol enumeration.
- `MicroTimeJsonConverter` - JSON converter for Kubernetes MicroTime.
- `NullableMicroTimeJsonConverter` - JSON converter for nullable MicroTime.
- `ServiceProviderExt` (static class) - Service-provider extensions for Kubernetes.


## ActualChat.App.Server

- `AppHost` - Main application host.
- `AssetVersionHelper` (static class) - Asset versioning utilities.
- `CommandLineHandler` (static class) - Command-line argument processing.
- `ConfigOnlyAppHost` (static class) - Minimal AppHost for configuration access.
- `LivelinessHealthCheck` - Kubernetes liveliness check.
- `ReadinessHealthCheck` - Kubernetes readiness check.
- `AggregateDbInitializer` - Orchestrates database initialization.
- `AggregateModuleInitializer` - Orchestrates module initialization.
- `DbInitializeOptions` (record) - Database initialization options.
- `GoogleCloudConsoleFormatter` - Google Cloud logging format.
- `LoggingBuilderExt` (static class) - Serilog configuration extensions.
- `ProcessIdLogEventEnricher` - Adds process ID to log events.
- `ThreadIdLogEventEnricher` - Adds thread ID to log events.
- `AppServerModule` - Main server module configuration.
- `ApplicationBuilderExt` (static class) - Middleware configuration extensions.
- `EndpointsExt` (static class) - Health and metrics endpoint mapping.
- `HostSettings` - Host configuration settings.


## ActualChat.App.Maui

- `Bars` - Platform status bar information.
- `CustomBlazorWebViewHandler` - Custom Blazor WebView handler.
- `FirebaseAnalyticsExt` (static class) - Firebase Analytics integration.
- `MainThreadExt` (static class) - Main thread scheduling extensions.
- `MainThreadTracker` (static class) - Main thread responsiveness monitoring.
- `MauiHostBuilderExtensions` (static class) - MAUI app builder extensions.
- `MauiRuntimeSettings` (static class) - Thread pool configuration.
- `ParentContainerAccessor` (record) - Parent service container access.
- `JSRuntimeErrors` (static class) - JS interop error factory.
- `MauiAudioInitializer` - Audio initialization (no-op for native).
- `MauiBrowser` (static class) - Platform browser URL opening.
- `MauiBrowserInfo` - Platform device detection.
- `MauiContactsPermissionHandler` - Contacts permission handling.
- `MauiLoadingUI` (static class) - Loading milestone tracking.
- `MauiMicrophonePermissionHandler` - Microphone permission handling.
- `MauiNotifications` - Push notification registration.
- `MauiReloadUI` - WebView reload.
- `MauiShare` - Platform share dialogs.
- `MauiSystemSettingsUI` - Platform system settings.
- `MediaMetadataUI` - Lock screen media metadata.
- `SafeJSObjectReference` - Safe JS object reference.
- `SafeJSRuntime` - JS runtime with disconnection handling.
- `PhoneParser` - Phone number parsing with LibPhoneNumbers.


## ActualChat.Maui

Cross-MAUI-app shared utilities (used by App.Maui and IosShareExt).

- `MauiModule` - DI module for MAUI shared services.
- `MauiSettings` - MAUI application settings.
- `MauiPreferences` - MAUI preferences storage.
- `MauiDiagnostics` - MAUI diagnostics utilities.
- `MauiHostNameRemapper` - Remaps hostnames for MAUI environments.
- `MauiBackgroundState` - MAUI app background/foreground state tracking.
- `WebAuth` - Web authentication settings for MAUI.
- `IconUI` - Icon UI management.
- `IconQueryExt` (static class) - Icon query extensions.
- `KvasarStoreSupport` (static class) - Kvasar store suspend handling and legacy SQLite cleanup.
- `AndroidIncomingShareSuggestions` - Android incoming share suggestions.
- `AndroidTaggedLogSink` - Android-specific Serilog sink with tags.
- `AndroidFirebaseCrashlyticsSink` - Firebase Crashlytics Serilog sink.
- `LoggerConfigurationXamarinExtensions` (static class) - Xamarin logger configuration extensions.
- `IosIncomingShareSuggestions` - iOS incoming share suggestions.
- `IosSharedSecureStorage` - iOS secure storage implementation.
- `IosVideoTranscoder` - iOS video transcoding.
- `OSLogLogger`, `OSLogLoggerProvider`, `AppleUnifiedLogSink` - iOS unified logging.
- `LoadInPlaceResultExt`, `AVAssetImageGeneratorExt`, `AVAssetTrackExt`, `CGSizeExt`, `CMTimeExt`, `NSErrorExt`, `NSItemProviderExt` (static classes) - iOS framework extensions.
- `LoggerConfigurationExtensions`, `LoggingBuilderExt` (static classes) - iOS logger configuration.
- `SentryExt` (static class) - Sentry integration extensions.


## ActualChat.App.Maui.IosShareExt

Standalone iOS share extension app and views.

- `ShareExtensionApplication` - Share extension app host (main entry).
- `ShareViewController` - Main share view controller.
- `ClientStartup` - iOS share extension client startup.
- `IosHub` - Main iOS hub for the share extension.
- `IosHubExt` (static class) - Extensions for IosHub.
- `IosShareExtensionModule` - iOS share extension DI module.
- `SessionInitializer` - Initializes session for the share extension.
- `ShareInputs` - Input handling for share data.
- `ShareStep` (enum) - Steps in the share workflow.
- `ShareUI` - Share UI state management.
- `ShareView` - Main share interface view.
- `SignInView` - Sign-in screen for the share extension.
- `SuccessView` - Success confirmation view.
- `ErrorView` - Error display view.
- `ContactView`, `ContactListView`, `ContactSelectionView`, `ContactIconView` - Contact display and selection views.
- `PlaceView`, `PlaceListView` - Place display views.
- `UploadProgressView` - Upload progress view.
- `IStatefulView`, `IStatefulView<T>`, `StatefulView`, `StatefulView<T>` - Stateful view abstractions.
- `ComputedStateView`, `ComputedStateView<T>`, `ComputedStateViewState<T>`, `CreateDefaultStateOptionsFactory<T>` - Computed-state view helpers.
- `NSId`, `NSId<TId>`, `NSHasId<T, TId>` - NSObject ID wrappers.
- `FusionBuilderExt`, `ServiceProviderExt`, `UICollectionViewCellRegistrationExt`, `UIKitExt`, `NSItemProviderExt` (static classes) - DI/UIKit/Fusion extensions.


## ActualChat.App.AotHelper

Tooling that emits AOT-friendly type "keeps" so MessagePack/STJ trimming-safe.

- `AotTypeGenerator` - Generates AOT type test code.
- `AotTypeTester` (abstract class) - Base tester for AOT type discovery.
- `IAotTypeTester` - Interface for AOT type testers.
- `ApiTypeTester` - Tests API-related types for AOT.
- `ComponentTypeTester` - Tests component types for AOT.
- `SerializableTypeTester` - Tests serializable types for AOT.
- `MessagePackByteSerializerDiscovery` - Discovers MessagePack byte serializers.
- `MessagePackFormatterDiscovery` - Discovers MessagePack formatters.
- `StjConverterDiscovery` - Discovers System.Text.Json converters.
- `StjKeepsGenerator` - Generates System.Text.Json converter keeps.


## ActualChat.App.Wasm

Blazor WebAssembly host shell for the client app. (Mostly bootstrap glue — no dedicated public types beyond `Program`.)


## ActualChat.App.AspireHost

.NET Aspire host for orchestrated local development. (Aspire app-host bootstrap — no dedicated public types beyond `Program`.)


## ActualChat.App.ConsoleClient

Console-based client used for diagnostics and integration testing. (CLI bootstrap — no dedicated public types beyond `Program`.)


## ActualChat.App.VideoLoadTest

Standalone load-testing tool for the video pipeline. (CLI bootstrap — no dedicated public types beyond `Program`.)


## ActualChat.Asr.Demo

Standalone demo app exercising `ActualChat.Asr`. (CLI bootstrap — no dedicated public types beyond `Program`.)


## ActualChat.MLSearch

(Empty marker project — no public types; implementation lives in `ActualChat.MLSearch.Service`.)

