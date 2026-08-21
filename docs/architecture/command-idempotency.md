# Command idempotency

API commands can be delivered more than once — a client retries after a timeout, a
connection drops and the queued command is resent, a shard migrates mid-flight. Without
protection, "add a reaction" or "create a chat" could run twice. This page describes how
Voxt makes an API command run **at most once** per client intent, and how the mechanism
stays compatible with clients that predate it.

**The short version.** Every client-callable command derives from `ApiCommand`, which
carries a client-generated `Uuid` (idempotency key). A server-side CommandR filter,
`ApiCommandDeduplicator`, claims each outermost command by `(SessionHash, Uuid)` in a
Redis-backed store, runs it once, and replays the stored result to any duplicate. It is
best-effort by design (~95% — an occasional double-run under network trouble is
acceptable), favouring **host-liveness over strict fencing**. Old clients that send the
pre-`Uuid` command layout are transparently migrated on the server, so the feature can roll
out before every command is migrated.

## The Uuid lives on the command

`ApiCommand` (`src/dotnet/Core/Commands/ApiCommand.cs`) is the base record for every
client-callable command:

```csharp
[DataContract, MessagePackObject]
public partial record ApiCommand : ISessionCommand, IApiCommand, IHasUuid
{
    [DataMember(Order = 0), Key(0)] public string Uuid { get; init; } = NewUuid();
    [DataMember(Order = 1), Key(1)] public required Session Session { get; init; }
    // ...
}
```

- The `Uuid` is at `Key 0` and **auto-generated**, so callers never pass it — a plain
  `new Reactions_React { Session = s, Reaction = r }` already has a fresh key. The generator
  is swappable (`ApiCommand.NewUuidGenerator`) for deterministic tests.
- The key is carried **on the command instance**, not in a Fusion/RPC header, so the intent
  is firmly identifiable end to end.
- A client that *wants* idempotency across retries reuses the same `Uuid` on the resend; a
  brand-new call gets a brand-new `Uuid` and is treated as a distinct intent.

## The deduplicator filter

`ApiCommandDeduplicator` (`src/dotnet/Core.Server/Commands/ApiCommandDeduplicator.cs`) is a
CommandR filter that runs on the **outermost** `ApiCommand` only. Its key is
`{Session.Hash}:{Uuid}` — the session hash (not the raw session) shortens the key and avoids
leaking or cross-contaminating sessions. The owner is this node's mesh id.

Per command it drives an `IIdempotencyStore`:

| Store state | Action |
|---|---|
| **New** (claim won) | Run the command, then `Complete` with the serialized result |
| **Completed** | Replay the stored result — the command never runs again |
| **InProgress**, owner **alive** | Wait for the owner's result (`WaitForResult`) |
| **InProgress**, owner **dead** | `TryReclaim` the claim and run it here |

Owner liveness comes from `MeshWatcher`: an `InProgress` claim whose owner node is no longer
`Online` is reclaimed immediately rather than waited out. A command that **fails** is not
cached — the claim is released so a same-`Uuid` retry re-runs it.

Empty-`Uuid` commands are skipped (`Uuid.Length > 0` guard) — see
[Old clients](#old-clients-graceful-degradation).

### Opting out — `INotDeduplicated`

A command that implements `INotDeduplicated` (`Core/Commands/ApiCommand.cs`) bypasses the filter
entirely. This is for high-frequency commands that would pay the store's two round-trips — and a
resident entry per call for `CompletedTtl` — for nothing:

| Command | Why |
|---|---|
| `UserPresences_CheckIn` | One per active client every `AwayTimeout * 0.75` (45 s), always a fresh `Uuid`, so it could never be replayed — and replaying one instead of running it would freeze presence |
| `ChatPositions_Set` | Debounced at 1 s per open chat while scrolling; fresh `Uuid` each time |
| `Uploads_Append` | One command per chunk (256 KB–4 MB) would fill the store during a single upload; a resend is handled by the handler's offset check instead |

The test that guards this is `NotDeduplicatedCommandRunsEveryTime` in `ApiCommandDeduplicatorTest`.
Before adding to the list, prefer evidence: a command whose `command.dedup.outcome` is all
`executed` and never `replayed` is a candidate — though today that metric isn't tagged by command
type, so the split isn't visible in prod yet.

## The Redis store

`IIdempotencyStore` (`Core.Server`) is implemented by `RedisIdempotencyStore` (`Redis`) on
`RedisDb<InfrastructureDbContext>` with key prefix `ApiCmdDedup`. `RedisDb.Database` applies that
prefix itself, so the store passes bare keys to every call — including the `keys` array of the Lua
script, which StackExchange.Redis prefixes too. Keys look like
`<instance>.infrastructure.ApiCmdDedup.<session hash>:<uuid>`.

- **Claim** — `SET NX` a marker `[InProgressTag][owner]`. Winning the `SET NX` is the claim.
- **Complete** — overwrite with `[CompletedTag][resultBytes]` (the MessagePack-serialized
  command result).
- **Reclaim** — a Lua compare-and-set: if the marker still names the expected dead owner,
  swap it to the new owner atomically; if it was completed meanwhile, return the result
  instead. This is the only multi-step operation, and it is atomic.

| TTL | Value | Meaning |
|---|---|---|
| `InProgressTtl` | 5 min | Guards only the live-but-slow case (a dead owner is reclaimed via liveness regardless of TTL). Must exceed the slowest realistic command. |
| `CompletedTtl` | 1 h | Dedup window — how long a completed result is replayed. Covers client retries / reconnects. |

There is **no fencing token**: a slow "first" owner could theoretically `Complete` over a
newer run. For idempotent commands this is harmless, and host death is far likelier than
Redis loss — hence liveness-based reclaim rather than strict fencing.

## Backward compatibility — the version-gated deserializer

Migrating a command to `ApiCommand` shifts its wire layout by one (`Uuid` inserted at
`Key 0`). During a rollout, an **old client** still serializes the *legacy* layout
(`Session @ 0`), while the new server expects `Uuid @ 0`. Rather than sniff the payload,
Voxt gates on the sender's **API version**.

`ApiCommandRpcArgumentSerializer` (`src/dotnet/Core/Serialization/`) decorates every
**server** inbound msgpack argument serializer (wired in `CoreModuleInitializer`, server
branch only — commands are client→server, so the client never needs it):

- The peer's API version is read per-call from the RPC handshake:
  `RpcInboundContext.Current.Peer.…Handshake.RemoteApiVersionSet[RpcDefaults.ApiScope]`.
- Peers **≥ `UuidVersion`** (pinned to the shipping release, `2.17`), backend peers
  (BackendScope, no ApiScope), and non-command args pass through the inner
  `RpcByteArgumentSerializerV4` verbatim — the common path, zero behaviour change.
- Peers **< `UuidVersion`** get their `ApiCommand`-typed argument migrated: the item's
  msgpack slice is measured, an empty `Uuid` is prepended (array header `+1`), and the
  transformed slice is deserialized into the current layout.

Only arguments whose type derives from `ApiCommand` are touched; everything else is
untouched. This is why `ApiCommand` lives in `Core` (so the decorator can name it) — it has
no `Api`-only dependencies.

**Exception — map payloads.** The transform only applies when the item's msgpack slice actually
starts with an array header (`IsArrayLayout`). A *named-key map* has no element to prepend, and it
needs none: a missing `Uuid` entry already reads back as `""`, exactly what the array path prepends.
Two kinds of map reach the server:

- **Commands with their own `[MessagePackFormatter]`** — `ServerKvas_*` and `ServerSettings_Set`.
- **Keyless formats** (`msgpack6k` / `msgpack6ck`), which serialize every member by name. These are
  what the TypeScript clients use, and `Uploads_Append` travels this way. Those peers advertise no
  `ApiVersionSet` today, so they land in the pass-through branch anyway — but the decorator must not
  rely on that: the moment TS gains versioning and reports anything below `UuidVersion`, a type check
  alone would drive a map into `ReadArrayHeader` and fail every such command.

See also [Serialization](./serialization.md) for the broader serializer landscape and the
MessagePack attribute convention.

## Old clients — graceful degradation

A migrated legacy command arrives with an empty `Uuid` (the prepended placeholder). The
deduplicator's `Uuid.Length > 0` guard therefore **skips** it: the command runs normally
every time, with no idempotency. Old clients keep working; they simply don't get dedup until
they upgrade. This was verified live — a real pre-`UuidVersion` client against a new server: reactions
work, the server logs the migration, and the deduplicator skips the empty-`Uuid` command.

## Observability

`IdempotencyMeters` (`src/dotnet/Core.Server/Diagnostics/`) publishes on
`CoreServerInstruments.Meter`:

| Instrument | Meaning |
|---|---|
| `command.dedup.outcome` `{executed,replayed,waited}` | Terminal outcome of a deduped command |
| `command.dedup.reclaim` | Claims reclaimed from a dead owner |
| `command.dedup.overrun` | In-progress marker expired without a result (possible double run) |
| `command.dedup.release` | Claims released after a failed command |
| `command.dedup.result_size` | Serialized result size |
| `command.dedup.store.duration` `{claim,complete}` | Store op latency (ms) |

Reclaims log at Info; overruns log at Warning.

## Design decisions

- **~95% is enough.** No perfect solution is required — an occasional double-run during
  network trouble is acceptable ("we're not transferring money"). Dedup state is **not** stored
  atomically with the operation's DB.
- **Host-liveness over fencing.** Mark which host owns execution; another host seeing the same
  command checks whether that owner is alive and, if not, redoes it. Host death is likelier than
  Redis loss.
- **Dedup for all commands except explicit opt-outs.** Every `ApiCommand` is deduped unless it
  implements `INotDeduplicated` (see [Opting out](#opting-out--inotdeduplicated)); only
  heavy/edit-type commands will opt into a client-side queue (deferred, see below). Cheap
  client-only actions (mute mic) need neither.
- **Version, not heuristics.** Backward compat is gated on the handshake API version, not on
  payload length or a caught exception.

## Writing an API command

Every client-callable command derives from `ApiCommand<TResult>`, with **no positional
constructor** — `Uuid` must never appear in a call site, and an all-`init` record is the only
shape that round-trips through MessagePack, Newtonsoft, and System.Text.Json alike:

```csharp
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Chats_RemoveEntry : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
    [DataMember(Order = 3), Key(3)] public required long LocalId { get; init; }
}
```

- **Own members start at `Key(2)`** — `Uuid` is 0 and `Session` is 1, both on the base.
- **`required`** for everything the caller must supply; drop it (and give a default) for
  optional members.
- Call sites use object initializers: `new Chats_RemoveEntry { Session = s, ChatId = c, LocalId = 1 }`.
- Don't deconstruct a command in its handler — read `command.ChatId` instead. Positional
  deconstruction died with the positional constructor, and it broke on every added member anyway.

## Deferred

- **Marker heartbeat** — extend the in-progress TTL while a command runs, for genuinely
  unbounded-duration commands. Not needed yet (the flat 5-min TTL covers realistic maxima);
  `command.dedup.overrun` would flag the gap.
- **Client `PartitionedCommandQueue`** — an opt-in-per-command client queue that orders and
  resends heavy/edit commands (resending the same `Uuid`, which the server then dedups).
