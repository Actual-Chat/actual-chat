# Content identifiers and maintenance partitions

## Decisions

Maintenance is centralized in the Users service. `MaintenanceKey` holds an arbitrary
string and a stable `ShardKey` routing value; it does not require a `ContentRef`.
The database key combines the full eight-digit routing value and the string, so
routing collisions do not conflate objects. `MaintenanceMode.None` means normal
operation; only active rows are stored.

There are 16 maintenance mesh shards. Each shard caches its entire set of active
rows, selected by the first hexadecimal digit of the routing value. Empty snapshots
are cached too. There is no separate partition layer. Warm negative lookups use the
snapshot and do not query the database. Existing Users service shard assignments
are unchanged.

## Identifier foundation

- `StringIdentifier` and `IStringIdentifier<T>` hold the shared string value,
  cached hash, parsing, comparison, and shard-key behavior.
- `ContentId : StringIdentifier` is the base for domain IDs that have a typed pair.
  Existing concrete identifier strings, equality, and serializer formats stay stable.
- `ContentRef : StringIdentifier` retains its original `ContentId`. It is not
  a `ContentId` subclass. The UI-only `ChatMessageKey` also inherits directly
  from `StringIdentifier`, without requiring a typed prefix registration.
  Country, email, emoji, interest, language, mention references, notification IDs,
  phone, stream/transcriber IDs, translation/source IDs, user-device IDs, emoji
  references, aliases, external-contact IDs, and upload IDs remain plain string identifiers.
- `ContentId.ContentRef` lazily creates its wrapper with `field ??=`.
- Typed values use `prefix:contentIdValue`, such as `u:abcdef` or `c:abcdef`.
  Prefixes and parsers are registered explicitly by the module that owns the IDs.
  Contact, conversation, and shared-location prefixes are `ct`, `cnv`, and `loc`.
  Chat subtypes share the chat prefix. Unknown prefixes fail parsing; unregistered
  ID types fail content-reference construction instead of acquiring unstable CLR names.
- One non-generic `IHasShardKey` exposes `ShardKey ShardKey { get; }`.
  String and symbol identifiers implement it by default; individual IDs own any
  routing rule based on a parent chat, owner, or other part of the identifier.
- `ShardKey` stores a full unsigned 32-bit integer without masking. Its standard
  string is eight lowercase hex digits. String hashing uses the shared xxHash3
  helper. Cache hex format strings and all one- and two-digit result strings.
- `ContentRef.ShardKey` delegates to `ContentId.ShardKey`, including the
  underlying ID's custom routing rule.
- Resolvers return `ShardKey` and prefer `IHasShardKey` before registered base
  resolvers. Reject registrations for any type whose inheritance implements that
  interface, including nullable wrappers of value-type providers. Built-in
  external types such as `Session` retain explicit registrations.
- Mesh and queue references carry `ShardKey`. At shard selection, retain the
  existing signed positive-modulo interpretation so the current 12-shard schemes
  keep their assignments even when a hash's high bit is set.
- Formatting and numeric extraction accept a digit count and take the highest
  digits first. Increasing the count extends the existing prefix with a suffix.
  Parsing accepts a string or character span containing one to eight hex digits,
  with no offset or digit-count parameters. Omitted low digits are zero:
  `Parse("ab")` is `ab000000`. Slice the input span before parsing when needed.
  The struct does not remember a prefix's length.
- The 32-bit value is a routing key, not a unique identifier. Existing mesh routing
  for other entities is unchanged.

## Content link migration

Replace the old numeric-tagged content identifier and its `ContentKind` enum with
`ContentRef`. The `ContentId` name now denotes the abstract base for IDs that
have a content reference.
`ContentLinksBackend` dispatches on the underlying content ID type and supports
only users, chats, chat entries, authors, and places. Other types fail explicitly.

The old type appears only in backend RPC and server-side OpenGraph metadata.
`RootServerPage` renders the title, description, and picture, and does not ship
the identifier to clients. Therefore the replacement uses the standard
`prefix:contentIdValue` format in every serializer; it needs no legacy type or API
method variants. Backend nodes must use the updated contract together.

Content links now follow the underlying identifier's routing rule. Author and
entry links therefore route by their chat, consistently with other uses of those
IDs. Do not register a separate content-reference routing override.

## Reactive maintenance reads

`IMaintenancesBackend.GetSnapshot` loads all active rows for one shard prefix.
Commands use `CreateOperationDbContext`, a key lock, and `Operation.MustStore(false)`.
On successful commit, `Operation.AddCompletionHandler` invalidates the local shard snapshot;
the delegating command does not use durable operation-framework invalidation. Per-object backend projections use
`ConsolidationDelay = 0` and value equality, suppressing notifications when an
unrelated object's status changes. All callers use `IMaintenancesBackend.Get`. Its protected `GetImpl` computation
consolidates the result inside the backend; the RPC entry point has no consolidation.

Routing uses the numeric high hexadecimal digit, from zero through fifteen.
Do not hash the prefix again or pass a left-aligned prefix directly to modulo.

## Chat maintenance

`ChatMaintenance` resolves direct status, then parent-thread and Place-root status.
The administrator-only `Chats_SetMaintenance` command toggles any existing chat.
Use `await debugUI.chatMaintenance(chatId, true)` to enable maintenance and pass
`false` to clear the direct status. An inherited status remains effective.

The chat displays the maintenance robot and a read-only footer. Client content
writes, pinning, chat changes, summarization, and live publishing are guarded.
Existing media streams check status before forwarding each frame, and UI workers
clear active recording, playback, and watching intent. Trusted backend operations
remain available. Persisted state survives server restarts.

Owner-driven imports/resets, anonymous-chat cleanup, and durable maintenance flows
are deferred. Destructive workflows will additionally need a drain barrier and
operation/version ownership before clearing maintenance; the manual toggle and
per-frame guards do not provide those guarantees.

## Reuse

### Existing abstractions to reuse

- `IStringLike<T>` and `StringLikeJsonConverter<T>`,
  `StringLikeNewtonsoftJsonConverter<T>`, `StringLikeMessagePackFormatter<T>`,
  and `StringLikeTypeConverter<T>` for identifier serialization.
- `GetXxHash3` from ActualLab.Core for deterministic hashing and `PositiveModulo` for existing mesh assignments.
- Existing identifier parse caches and `StringIdentifierTestBase<T>` / `ContentIdTestBase<T>` tests.
- `ShardScheme`, `ShardRef`, `QueueShardRef`, `GenericInstanceCache`, mesh ownership, and the Users backend hosting role.
- Fusion compute-method consolidation and value equality.
- `DbServiceBase<UsersDbContext>`, operation events, `FlowHub`,
  `IFlowBackend`, and `ChatsBackend_Change` for persistence and durable work.

`TypeMap` and `TypeMapper` map CLR implementation types, not stable textual
prefixes and parsers. They do not fit content-reference registration. The existing
`MentionKind` prefix/parser approach informs the explicit registration design.

### Reusability of new components

`StringIdentifier`, `ContentId`, `ContentRef`, and `ShardKey` apply beyond maintenance.
Put them in Core, rather than Users or Chat. Prefix registrations remain in the
module that owns each concrete identifier.

Generic maintenance models belong in Core; backend contracts and shared chat guards
live in Core.Server. Keep Users database persistence in
Users.Service and chat deletion/guard policy in Chat.Service. This shares reusable
infrastructure without making Core depend on users, chats, EF Core, or the UI.

## Validation

The identifier slice must cover existing serializer compatibility, typed round trips
and retained object identity, disambiguation of equal raw values belonging to different
types, malformed inputs, truncation, all hex slice positions, and stable hash vectors.
Regenerate AOT metadata and build the affected projects.

The subsequent maintenance slice needs tests for shard snapshot invalidation,
unchanged per-object results suppressing notifications, empty snapshots, durable
restart/retry, stale flow completion, client guards, and backend operations remaining
available. These service and flow tests are separate from the identifier tests.
