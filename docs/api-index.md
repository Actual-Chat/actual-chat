# API Index (Condensed)

A condensed reference of the most important types in ActualChat.
Use this to find existing abstractions before writing new code.
See also: [Full C# API Index](api-index-full.md), [TypeScript API Index](api-index-ts.md).


## Core (`ActualChat.Core`)

### Identifiers
- `StringIdentifier<T>` — base for string-based identifiers (UserId, ChatId, etc.)
- `SymbolIdentifier<T>` — base for Symbol-based identifiers

### Async & Concurrency
- `Debouncer<T>` — delays action execution until interval passes without new items
- `Throttler<T>` — limits action execution to at most once per interval
- `IAsyncObservable<T>` — observable with channel-based async subscribers
- `TaskSerializer` — serializes task execution to run sequentially
- `ChannelMuxer<TKey, TItem>` — multiplexes multiple channels into one

### In-Memory Search (`ActualChat.Search`)
- `MemSearchDocument` / `MemSearchQuery` — typed match blob + parsed query (tokenize, `IsMatch`, coverage score, `GetMatchParts` highlight ranges)
- `SearchMatch` / `SearchMatchPart` — matched text + rank + highlight parts (explicit, or lazy from a `MemSearchQuery`)

### Rate Limiting (`ActualChat.Resilience`)
- `IRateLimiter<TKey>` / `IRateLimiter<TKey, TBudget>` — one check per key, returns the retry delay or null
- `SlidingWindowBudget` — limit + window pair a limiter charges against
- `RateLimitExceededException` — thrown when a limit is exceeded; carries `RetryDelay`
- `RateLimitPolicy` / `RateLimitRule` (`ActualChat.Core.Server`) — the configured limits; charge every dimension of a call

### Collections & Caching
- `LruCache<TKey, TValue>` — thread-safe LRU cache
- `BlockRingBuffer<T>` — ring buffer with block-level operations
- `SharedResourcePool<TResource>` — pooled resource lease management

### State & Change Tracking
- `Mutable<T>` — mutable value holder with change notifications
- `Change<T>` (record struct) — represents create/update/remove operation
- `IDiff`, `DiffEngine` — diff processing for entity changes

### Key-Value Store (Kvas)
- `IKvas` — key-value store interface
- `IKvasStore` — an `IKvas` that owns its storage (list/flush/clear)
- `BatchingKvas` — batching KVAS with delayed persistence
- `KvasarKvas` — encrypted file-based KVAS store on `ActualLab.Kvasar` (MAUI)
- `LocalSettings` — local settings storage via KVAS
- `StoredState<T>` — state backed by KVAS

### Workers
- `WorkerBase` — long-running background worker (start/stop/cancel)
- `ActivatedWorkerBase` — worker with activation tracking

### Error Handling
- `StandardError` — factory for standard errors (NotFound, Unauthorized, etc.)
- `ExternalError`, `InternalError` — categorized error types

### Hosting
- `HostInfo` (record) — host environment information
- `HostKind`, `HostRole` (enum) — host type and role
- `IModuleInitializer` — module initialization interface

### Features
- `Features` — aggregates client and server feature flags
- `FeatureDef<T>` — feature flag definition

### Security
- `ISecureTokens` — secure token operations
- `TrueSessionResolver` — session resolution


## Core Audio (`ActualChat.Core.Audio`)

- `AudioProcessingModule` — Web Audio API audio processing wrapper
- `VoiceActivityDetector`, `OnnxVoiceActivityDetector` — VAD implementations


## Database (`ActualChat.Db`)

- `IDbEntity` — database entity marker
- `DbModule` — database module configuration
- `DbInitializer` — database initialization interface


## Redis (`ActualChat.Redis`)

- `RedisModule` — Redis module configuration
- `RedisMeshLocks` — Redis-based distributed locks
- `RedisSlidingWindowRateLimiter` — sliding window rate limiter (`IRateLimiter<string, SlidingWindowBudget>`)
- `RedisRateLimitPolicy` — builds the API host `RateLimitPolicy` (commands local, the rest via Redis)


## Kubernetes (`ActualChat.Kubernetes`)

- `KubernetesModule` — Kubernetes integration module
- `KubeMeshLocks` — Kubernetes lease-based distributed locks
- `KubeServices` — Kubernetes service discovery
- `KubeLeaseClient` — leader-election leases
- `IKubeInfo`, `KubeInfo` — cluster info


## Backend Markers (`ActualChat.Backend`)

- `IRequiresThisNode`, `IRequiresRandomShard`, `IRequiresZeroShard` — service shard placement markers
- `ShardScheme`, `ShardSchemeFlags` — sharding scheme configuration


## API Types (`ActualChat.Api`)

### Core Identifiers
- `UserId` — user account identifier
- `ChatId` — chat identifier (GroupChatId, PlaceChatId, PeerChatId, ThreadChatId)
- `AuthorId` — author within a chat
- `PlaceId` — place (community) identifier
- `MediaId` — media content identifier
- `ContactId` — contact identifier

### Chat & Messaging
- `Chat` (record) — chat information (kind, title, rules, settings)
- `Author` (record) — author in a chat (user link, avatar, rules)
- `ChatEntry` — base for chat entries (TextEntry, SystemEntry)
- `TextEntry` (record) — text message with markup, attachments, reactions
- `Place` (record) — community/place information
- `Role` (record) — role definition with permissions; system roles are `Anyone`,
  `Guest`/`User`/`AnonymousUser` (automatic membership), and `Moderator`/`Owner`
  (explicit membership, appointed via `Authors_ChangeRole`)
- `ChatPermissions` / `PlacePermissions` (flags enums) — bit values must stay
  aligned (`Places.ToPlaceRules` casts between them); `ChatPermissionsExt.AddImplied`
  is the implication closure, where `Moderate` implies `EditProperties` + `EditMembers`
  and `Owner` implies `Moderate`
- `AuthorRules` / `PlaceRules` (records) — an actor's resolved permissions in a
  chat / place; test them via `IsOwner()`, `CanModerate()`, `CanRead()`, etc.

### Markup
- `IMarkupParser` — parses text into markup tree
- `Markup` — base markup element (PlainText, Url, Mention, CodeBlock, etc.)
- `MarkupFormatter` — formats markup to plain text
- `MentionExtractor` — extracts mentions from markup
- `MentionMarkup` — base mention; subclasses `AuthorMention`, `UserMention`, `ChatMention`, `PlaceMention`, `EmojiMention` carry pre-resolved data
- `MentionRef` — `<prefix>:<localId>` reference; dispatches via `MentionKind` registry to an `IMentionTarget` (its `.Target`)
- `MentionKind` — registry of mention prefixes (`a`/`u`/`c`/`p`/`e`; `a` author is legacy/anonymous-only)
- `EmojiRef` — URL-encoded emoji mention target (`EmojiRef.FromText` encodes raw text)
- `IMentionResolver` (non-generic) — `Apply(Markup, ct)` markup-tree rewriter that enriches mentions with cached data (was `MentionNamer`)
- `MentionCandidate` — unified candidate (User/Chat/Emoji) returned by the index
- `MentionCandidateFilters` — category predicates + `KindRank` + `FilterAndRank(filter, query, limit, recencyScores?)` for the picker (recents boost ranking)
- `RecentMentions` / `RecentGifs` — per-user `StoredSettings` (MessagePack-only) tracking recently picked mentions (recency+frequency score) and GIFs (MRU); surfaced via `RecentMentionsUI` / `RecentGifsUI` synced-state services

### Media
- `Media` (record) — media metadata (content type, size, dimensions)
- `Picture` (record) — picture with multiple sizes
- `LinkPreview` (record) — preview of linked content

### Users & Accounts
- `Account` (record) — user account (name, avatar, status)
- `Avatar` (record) — user avatar configuration
- `Presence` (record) — online/away/offline status

### Audio
- `AudioFrame` (record) — audio frame with format and data
- `Transcript` — audio transcript with timing
- `TranscriptionEngine` (enum) — transcription engine type

### Contacts
- `Contact` (record) — contact information


## Service Contracts (`ActualChat.Api.Contracts`)

### Chat Services
- `IChats` — chat CRUD, listing, rules
- `IAuthors` — author management within chats; `Authors_ChangeRole` appoints/removes Owners and Moderators
- `IPlaces` — place (community) management; `ListOwnerIds`/`ListModeratorIds` forward to the place root chat
- `IRoles` — role management; `ListOwnerIds`/`ListModeratorIds` mask anonymous members from non-owner callers (use `IRolesBackend` when that masking would be a hole)
- `IReactions` — message reactions
- `IMentions` — mention queries

### User Services
- `IAccounts` — account management
- `IAvatars` — avatar management
- `IUserPresences` — presence tracking

### Contact Services
- `IContacts` — contact management
- `IExternalContacts` — device contact sync

### Media Services
- `IUploads` — file upload handling
- `IMediaLinkPreviews` — link preview generation

### Other Services
- `INotifications` — push notifications
- `IInvites` — invite link management
- `ISearch` — full-text search
- `IStreamClient` — audio streaming


## Backend Contracts (`*.Contracts`)

Backend interfaces follow the pattern `I{Service}Backend` for internal service communication:
- `IChatsBackend`, `IAuthorsBackend`, `IPlacesBackend`, `IChatThreadsBackend`, `IChatEntryLanguagesBackend` — chat backends
- `IAccountsBackend`, `IAvatarsBackend`, `ISessionTemporalsBackend`, `UserScopedKvasBackend` — user backends
- `IContactsBackend` — contact backend
- `IMediaBackend`, `IMediaProgressBackend`, `IUploadsBackend` — media backends
- `INotificationsBackend` — notification backend
- `IStreamingBackend`, `ILiveBackend`, `ILiveAudioBackend`, `ILiveVideoBackend`, `IVideoStreamingBackend` — streaming backends


## Server Infrastructure (`ActualChat.Core.Server`)

### Flows (Long-Running Operations)
- `Flow` — base for long-running operations with persistence
- `PeriodicFlow` — periodic execution flow
- `IndexingFlow` — indexing with cursor tracking
- `ThrottledFlow` — throttled execution
- `FlowHub` — manages flow instances

### Queues
- `IQueues` — queue service interface
- `IQueueProcessor` — processes queued commands
- `NatsQueues` — NATS-based queue implementation
- `QueuedCommand` (record) — command in queue

### Blob Storage
- `IBlobStorages` — blob storage service
- `IBlobStorage` — individual blob storage operations
- `GoogleCloudBlobStorage` — Google Cloud Storage implementation

### Sharding
- `ShardMap` — maps keys to shards
- `ShardWorker` — shard-aware worker
- `ShardedDbServiceBase` — base for sharded database services

### Mesh & Clustering
- `MeshNode` — node in mesh cluster
- `IMeshLocks` — distributed locking
- `MeshWatcher` — monitors mesh state changes

### Media Processing
- `IUploadProcessor` — processes uploaded files
- `IMediaProcessor` — processes media content
- `IContentSaver` — saves content to blob storage

### AI Helpers
- `IAnthropicClient` — Anthropic Claude API client
- `IPromptHelpers`, `PromptTemplate` — reusable prompt templates


## UI Core (`ActualChat.UI`)

- `UICoreModule` — UI core module
- `ChunkedFileUploader` — resumable file uploads with retry
- `AppRemoteComputedCache` — client-side computed value cache
- `KvasarRemoteComputedCache` — Kvasar-backed remote computed cache (MAUI)


## Blazor UI (`ActualChat.UI.Blazor`)

### Base Types
- `ComponentBase<THub>` — Blazor component base with service shortcuts
- `ComputedStateComponent<TState, THub>` — component with Fusion computed state
- `UIServiceBase<THub>` — base for UI services
- `UIWorkerBase<THub>` — base for UI background workers

### Core Services
- `UIHub` — central hub providing access to all UI services
- `History` — browser navigation history management
- `ModalUI` — modal dialog management
- `ToastUI` — toast notification management
- `PanelsUI` — left/middle/right panel management
- `AccountUI` — account state and authentication flow
- `ThemeUI` — theme (light/dark) management
- `ReconnectUI` — RPC connection state monitoring

### Components
- `VirtualList<T>` — abstract base of the two virtualized lists (data source, JS bridge, visibility)
- `FiniteList<T>` — known length, uniform items, real scrollbar (the chat list)
- `InfiniteList<T>` — unbounded, no scrollbar, anchored (the chat view, content tabs, log view)
- `Menu<T>` — menu component
- `ModalRef` — modal dialog reference
- `PermissionHandler` — permission request handling

### Caching
- `WebRemoteComputedCache` — IndexedDB-based remote computed cache


## Blazor App (`ActualChat.UI.Blazor.App`)

### Core Services
- `AppUIHub` — extended UI hub with chat-specific services
- `ChatUI` — chat selection, read positions, chat state
- `ChatAudioUI` — audio listening/recording state
- `ChatListUI` — chat list filtering and sorting
- `SearchUI` — unified search across chats
- `LanguageUI` — language preferences
- `OnboardingUI` — user onboarding flow
- `LiveStreamUI` — live streaming management
- `LocalSearchUI` — in-memory local search service; public `ListXxx` compute methods (contacts, users, authors, chats, places) return natural types, `ListMentionCandidates` unions them for the @-mention picker

### Playback
- `ChatPlayers` — orchestrates audio playback across chats
- `ChatPlayer` — plays audio entries (HistoricalChatPlayer, RealtimeChatPlayer)

### Recording
- `AudioRecorder` — audio recording component
- `RecorderStateHub` — recording state management

### Message Sending
- `SendingMessages` — manages message sending with retry logic
- `AttachmentsController` — attachment management

### Components
- `ChatView` — main chat view component
- `ChatMessage` — message display
- `MarkupView` — markup rendering
- `MarkupEditor` — markup editing
- `ChatList` — chat list component
- `EditMembersUI` — member editing utilities


## ML / AI Services

### Chat ML (`ActualChat.Chat.ML`)
- `IConversationSummarizer`, `ConversationSummarizer` — AI conversation summarization
- `IChatDigestSummarizer` — chat digest summarization
- `IThreadInsightExtractor` — thread insight extraction
- `IEmbeddingsCalculator` — text embeddings
- `IEntryGroupExtractor`, `EntryGroupBuilder` — group entries for ML
- `RateLimitedChatCompletionService` — rate-limited LLM calls
- `OpenAITranscriber` — OpenAI-based ASR
- `TokenEstimator` — token-count estimator

### ML Search (`ActualChat.MLSearch.Service`)
- `Search`, `SearchBackend` — OpenSearch-backed implementations of `ISearch`/`ISearchBackend`
- `OpenSearchSettings`, `OpenSearchConfigurator` — OpenSearch client setup
- `EntryIndexingFlow`, `AccountIndexingFlow`, `PlaceIndexingFlow`, `UserContactIndexingFlow`, `PlaceContactIndexingFlow`, `GroupIndexingFlow` — indexing flows

### ASR (`ActualChat.Asr`)
- `ParakeetModel`, `ParakeetModelDownloader` — NVIDIA Parakeet ASR
- `TdtDecoder` — Token-and-Duration Transducer decoder
- `ProgressiveStreamingHandler` — progressive ASR streaming

### Flows (`ActualChat.Flows.Service`)
- `FlowBackend`, `FlowsServiceModule` — flow execution backend


## Email Templates (`ActualChat.Mjml.Blazor`, `ActualChat.Users.Templates`)

- `Mjml*` Blazor components — MJML email-template builder
- `BlazorRenderer`, `DigestArgs` — render user-facing email templates


## Server Application (`ActualChat.App.Server`)

- `AppHost` — main application host
- `AppServerModule` — server module configuration
- `HostSettings` — host configuration settings
- `AggregateDbInitializer` — orchestrates database initialization
- `ReadinessHealthCheck`, `LivelinessHealthCheck` — Kubernetes health checks


## Other App Hosts

- `ActualChat.App.Wasm` — Blazor WebAssembly client host
- `ActualChat.App.AspireHost` — .NET Aspire orchestrated dev host
- `ActualChat.App.ConsoleClient` — diagnostic CLI client
- `ActualChat.App.VideoLoadTest` — video pipeline load tester
- `ActualChat.App.AotHelper` — generates AOT-friendly type "keeps" for trimming
- `ActualChat.UI.App` — `AppServerInstanceSelector`, `IncomingShareSuggestions`, `VideoTranscoder`


## MAUI Shared (`ActualChat.Maui`)

- `MauiModule`, `MauiSettings`, `MauiPreferences`, `MauiDiagnostics`, `MauiHostNameRemapper`, `MauiBackgroundState`
- `KvasarStoreSupport` — Kvasar store suspend handling + legacy SQLite cleanup
- Platform-specific extensions for Android (`Android*`) and iOS (`Ios*`, `OSLog*`, `*Ext` for AVFoundation/UIKit)


## iOS Share Extension (`ActualChat.App.Maui.IosShareExt`)

Standalone iOS share-extension app:
- `ShareExtensionApplication`, `ShareViewController` — extension entry points
- `IosHub`, `IosShareExtensionModule`, `SessionInitializer` — DI / session
- `ShareView`, `SignInView`, `ContactSelectionView`, `UploadProgressView`, etc. — extension UI
- `IStatefulView<T>`, `ComputedStateView<T>` — Fusion-style stateful UIKit views


## MAUI Application (`ActualChat.App.Maui`)

### Core
- `CustomBlazorWebViewHandler` — Blazor WebView with service injection
- `Bars` — platform status bar information
- `MauiRuntimeSettings` — thread pool configuration

### Services
- `MauiBrowserInfo` — platform device detection
- `MauiShare` — platform share dialogs
- `MauiNotifications` — push notification registration
- `MauiLoadingUI` — loading milestone tracking

### Permissions
- `MauiMicrophonePermissionHandler` — microphone permission
- `MauiContactsPermissionHandler` — contacts permission

### JS Interop
- `SafeJSRuntime` — JS runtime with disconnection handling
- `SafeJSObjectReference` — safe JS object reference


## Key Patterns

### Service/Backend Split
Most services have two interfaces:
- Public: `IChats`, `IAccounts`, etc. — for client use
- Backend: `IChatsBackend`, `IAccountsBackend`, etc. — for internal server use

### Module System
Functionality organized into modules registered in DI:
- `CoreModule`, `CoreServerModule` — core infrastructure
- `ChatServiceModule`, `UsersServiceModule` — domain services
- `BlazorUICoreModule`, `BlazorUIAppModule` — UI modules

### Computed State
UI uses Fusion's computed state for reactive updates:
- `ComputedStateComponent<TState, THub>` for components
- `[ComputeMethod]` attribute on service methods

### Database Models
Database entities prefixed with `Db`:
- `DbChat`, `DbAuthor`, `DbTextEntry` — chat entities
- `DbAccount`, `DbAvatar` — user entities
