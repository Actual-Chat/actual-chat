# Object identifiers and maintenance partitions

## Decisions

Object maintenance is centralized in a persisted Users service. Maintenance mode is
an enum with `None` meaning normal operation. Active rows identify objects with a
`TypedObjectId`; the full typed value is the database key, so hash collisions do
not conflate objects.

Maintenance initially has 16 mesh shards and 256 independently cached data
partitions. The first hex digit of an object's `PartitionKey` selects its mesh
shard. The first two digits select its data partition. Each mesh shard therefore
owns 16 data partitions; a physical node can own multiple mesh shards.

Only active maintenance rows are stored. Missing rows mean `None`. Empty partition
snapshots are cached as well. Database placement remains independent of mesh
ownership.

## Identifier foundation

- `StringIdentifier` and `IStringIdentifier<T>` hold the shared string value,
  cached hash, parsing, comparison, and partition behavior.
- `ObjectId : StringIdentifier` is the base for domain IDs that have a typed pair.
  Existing concrete identifier strings, equality, and serializer formats stay stable.
- `TypedObjectId : StringIdentifier` retains its original `ObjectId`. It is not
  an `ObjectId` subclass. The UI-only `ChatMessageKey` also inherits directly
  from `StringIdentifier`, without requiring a typed prefix registration.
- `ObjectId.TypedId` lazily creates its wrapper with `field ??=`.
- Typed values use `prefix:objectValue`, such as `u:abcdef` or `c:abcdef`.
  Prefixes and parsers are registered explicitly by the module that owns the IDs.
  Chat subtypes share the chat prefix. Unknown prefixes fail parsing; unregistered
  ID types fail typed-wrapper construction instead of acquiring unstable CLR names.
- `PartitionKey` stores one integer, masked to 24 bits. Its standard string is
  six lowercase hex digits. Object and symbol IDs derive it from the existing
  stable xxHash3 helper applied to their untyped value.
- `TypedObjectId.PartitionKey` delegates to `ObjectId.PartitionKey`; adding a type
  prefix must not change the partition.
- Formatting accepts a prefix length, start/length, or `Range`. Parsing accepts
  one to six hex digits and an optional digit offset. Omitted digits are zero:
  `Parse("ab")` is `ab0000`; `Parse("cd", 2)` is `00cd00`.
  The struct does not remember a slice's length.
- The 24-bit value is a routing key, not a unique identifier. Existing mesh routing
  for other entities is unchanged.

## Content link migration

Replace `ContentId` and its `ContentKind` enum with `TypedObjectId`.
`ContentLinksBackend` dispatches on the underlying object ID type and supports
only users, chats, chat entries, authors, and places. Other types fail explicitly.

The old type appears only in backend RPC and server-side OpenGraph metadata.
`RootServerPage` renders the title, description, and picture, and does not ship
the identifier to clients. Therefore the replacement uses the standard
`prefix:objectValue` format in every serializer; it needs no legacy type or API
method variants. Backend nodes must use the updated contract together.

Preserve content-link shard routing by hashing the underlying raw ID value.
Do not substitute the typed prefix or a type-specific resolver such as an author's
chat ID.

## Reactive maintenance reads

The backend's partition compute method loads all active rows in one data partition.
A maintenance change invalidates that partition immediately. This method has no
consolidation delay.

Per-object status projections depend on the snapshot and use
`ConsolidationDelay = 0`. They recompute after a partition invalidation and notify
their own consumers only if that object's status changed. This avoids propagating
an unrelated object's maintenance changes throughout the cluster.

Fusion forbids consolidation on RPC-exposed methods of a distributed service.
Keep the consolidated projection server-local or protected, with a distributed
entry point delegating to it. The status result must have value equality and must
not include a partition-wide version that changes for unrelated objects.

Use the existing `ShardKey` wrapper with the numeric first hex digit when routing
partition reads and writes. Do not hash a prefix string again or route a left-aligned
24-bit prefix through modulo directly; both would break the intended grouping.

## Maintenance lifecycle

A backend command persists the mode and a durable flow-start/resume event in the
same operation. An object maintenance flow performs the work through trusted backend
commands. Maintenance guards apply to client-facing operations; backend operations
remain available.

The chat layer immediately treats chats with `AllowAnonymousAuthors` as under
maintenance while durable registration is pending. Registration happens through
commands/events and a scan of existing chats, without writes in computed reads.
Deletion uses the existing backend chat-removal command.

Only chats with anonymous mode enabled are eligible for this deletion. Ordinary
chats containing anonymous authors are preserved for separate handling. Account
identities behind anonymous authors must never be exposed.

A flow failure leaves maintenance active. Expected versions and operation IDs prevent
an older flow from clearing a newer maintenance operation. Client streams must
quiesce before destructive work proceeds.

## Reuse

### Existing abstractions to reuse

- `IStringLike<T>` and `StringLikeJsonConverter<T>`,
  `StringLikeNewtonsoftJsonConverter<T>`, `StringLikeMessagePackFormatter<T>`,
  and `StringLikeTypeConverter<T>` for identifier serialization.
- `GetXxHash3` from ActualLab.Core for deterministic partition hashing.
- Existing identifier parse caches and `StringIdentifierTestBase<T>` / `ObjectIdTestBase<T>` tests.
- `ShardScheme`, `ShardKey`, mesh ownership, and the Users backend hosting role.
- Fusion compute-method consolidation and value equality.
- `DbServiceBase<UsersDbContext>`, operation events, `FlowHub`,
  `IFlowBackend`, and `ChatsBackend_Change` for persistence and durable work.

`TypeMap` and `TypeMapper` map CLR implementation types, not stable textual
prefixes and parsers. They do not fit typed-ID registration. The existing
`MentionKind` prefix/parser approach informs the explicit registration design.

### Reusability of new components

`StringIdentifier`, `ObjectId`, `TypedObjectId`, and `PartitionKey` apply beyond maintenance.
Put them in Core, rather than Users or Chat. Prefix registrations remain in the
module that owns each concrete identifier.

Generic maintenance models also belong in Core; backend contracts and generic flow
coordination can live in Core.Server. Keep Users database persistence in
Users.Service and chat deletion/guard policy in Chat.Service. This shares reusable
infrastructure without making Core depend on users, chats, EF Core, or the UI.

## Validation

The identifier slice must cover existing serializer compatibility, typed round trips
and retained object identity, disambiguation of equal raw values belonging to different
types, malformed inputs, truncation, all hex slice positions, and stable hash vectors.
Regenerate AOT metadata and build the affected projects.

The subsequent maintenance slice needs tests for independent partition invalidation,
unchanged per-object results suppressing notifications, empty snapshots, durable
restart/retry, stale flow completion, client guards, and backend operations remaining
available. These service and flow tests are separate from the identifier tests.
