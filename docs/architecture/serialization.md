# Serialization

Voxt serializes the same types through several serializers at once. This page describes
which serializer owns which path, what each one actually honors, and the attribute
convention we are converging on.

Everything in the "What each serializer honors" table was verified empirically against
this repository's serializer configuration, not taken from upstream documentation.

## The serializers and what each one owns

| Serializer | Owns | Authoritative for |
|---|---|---|
| **Newtonsoft.Json** | Operation log and event log | `DbOperation.CommandJson`, `DbOperation.ItemsJson`, `DbEvent` |
| **System.Text.Json** | Blazor → JavaScript interop | Everything crossing the JS boundary |
| **MessagePack** | RPC wire format | All RPC payloads (`msgpack6`, `msgpack6c`, `msgpack6k`, `msgpack6ck`) |
| **MemoryPack** | Legacy reads only | Pre-existing DB blobs; never written for new data |

### Newtonsoft.Json — the operation log

`DbOperation.Serializer` and `DbEvent.Serializer` are both `NewtonsoftJsonSerializer.Default`
(`ActualLab.Fusion.EntityFramework/Operations/`). The operation log is what makes invalidation
and operation replay work, so:

> **Every backend command — and every type reachable from one — must be
> Newtonsoft.Json-serializable.**

This applies to *non-delegating* commands only. API commands that merely delegate to a
backend command are never persisted, so they carry no such requirement. In practice the
constraint reaches most identifiers and domain models, because backend commands reference them.

Relevant settings (`NewtonsoftJsonSerializer.DefaultSettings`):

- `TypeNameHandling.Auto` — polymorphic payloads carry `$type`
- `NullValueHandling.Ignore`
- `ContractResolver = new DefaultContractResolver()` — **this is the resolver that honors
  `[DataContract]`**, and it is the root of the attribute problem described below

### System.Text.Json — the JS boundary

Blazor uses System.Text.Json for everything it hands to JavaScript. The server also *offers*
`SystemJsonV5` / `SystemJsonV5NP` as RPC formats, but clients advertise MessagePack only, so
in practice STJ over RPC is unused.

STJ output is camelCase.

### MessagePack — the RPC wire format

The only binary format written today. Server offers `msgpack6`, `msgpack6c`, `msgpack6k`,
`msgpack6ck`; clients offer `msgpack6` / `msgpack6c` (`CoreModuleInitializer`).

### MemoryPack — legacy reads only

MemoryPack is **never written** for new data. It survives on exactly two read paths, both
for blobs already sitting in the database:

- **KVAS** — `KvasSerializer` reads marker byte `0x0` as MemoryPack, `0x1` as MessagePack,
  and always *writes* MessagePack (unless `PreferMemoryPack` is set).
- **Flows** — `FlowData.FlowSerializer` is a `VersionedByteSerializer` whose format 0 is
  MessagePack, with MemoryPack as the legacy fallback leg.

The rule that follows: **MemoryPack attributes belong only on types whose bytes are already
persisted in the database.** Nothing else needs them, and nothing new should acquire them.

## What each serializer honors

Verified by round-tripping probe types through this repo's actual serializer instances.

| Attribute | Newtonsoft.Json | System.Text.Json | MessagePack |
|---|---|---|---|
| `[DataContract]` | **Yes — switches the type to opt-in** | Ignored | Only via the dynamic resolver; **not under AOT** |
| `[DataMember(Order)]` | Controls output order | Ignored | Ignored when `[MessagePackObject]` is present |
| `[DataMember(Name)]` | Renames the property | Ignored | — |
| `[IgnoreDataMember]` | Excludes (with or without `[DataContract]`) | **Ignored — member is still serialized** | Does not satisfy `MsgPack004` |
| `[JsonIgnore]` (STJ) | — | Excludes | — |
| `[Newtonsoft.Json.JsonIgnore]` | Excludes | — | — |
| `[Key(N)]` / `[IgnoreMember]` | — | — | Authoritative |

Unqualified `JsonIgnore` is **System.Text.Json's** — `System.Text.Json.Serialization` is a
global using (`Directory.Build.props`). Newtonsoft's must always be written out in full.

### The three findings that matter

**1. `[DataContract]` silently flips Newtonsoft into opt-in mode.**

```
[DataContract] present:  {"Marked":"m"}                              // Unmarked dropped
[DataContract] absent:   {"Marked":"m","Unmarked":"u"}
```

On a `[DataContract]` type, adding a public property without `[DataMember]` means it is
silently *not* persisted to the operation log. Nothing warns you.

**2. `[Key]` beats `[DataMember(Order)]` — the DataContract annotations are dead weight.**

A probe type carrying deliberately *contradictory* orders (`First` = `DataMember(Order=1)`
+ `Key(0)`, `Second` = `DataMember(Order=0)` + `Key(1)`) produced byte-identical output to
the same type with the DataContract attributes removed:

```
Both attribute sets:          92A161A162
MessagePack attributes only:  92A161A162
```

So on any type that already has `[MessagePackObject]` + `[Key(N)]`, removing
`[DataContract]`/`[DataMember]` **cannot change the MessagePack wire format**.

**3. System.Text.Json ignores `[IgnoreDataMember]` entirely.**

```
SystemJson: {"marked":"m","unmarked":"u","ignored":"i"}   // [IgnoreDataMember] had no effect
```

A member excluded from MessagePack and Newtonsoft via `[IgnoreDataMember, IgnoreMember]` is
still emitted by STJ, which evaluates the getter. That was not merely a leak: eight types threw
outright when serialized, because the getter dereferences state the payload doesn't carry —

```csharp
[IgnoreDataMember, IgnoreMember]
public ChatId ShardKey => Attachments.Length > 0
    ? Attachments[0].EntryId.ChatId
    : throw new ArgumentException("No attachments provided", nameof(Attachments));
```

96 `ShardKey` properties across 45 files were fixed by adding the explicit trio, along with
`EntryRef.ChatId` and `RelatedEntryRef.EntryId` — the same defect under different names — and
then 34 more computed properties under other names (`FlowTypeStat.Total`,
`MediaProgress.HasFailed`, `LiveSessionState.IsCall`, the `Flow` runtime properties, …).

Only **public properties** can leak this way: System.Text.Json serializes neither non-public
members nor fields by default, and types handled wholesale by a type-level converter never
consult member attributes at all. Counting without those filters badly overstates the problem.

The sweep is complete — no public property is now excluded from MessagePack but emitted by
System.Text.Json — and it was format-neutral: MessagePack verified byte-identical across all
354 types, while 28 types' JSON shrank by exactly the leaked members.

## Target convention

**Support all three live serializers on everything serializable, using each serializer's own
attributes, and drop the `System.Runtime.Serialization` ones.**

`[DataContract]`/`[DataMember]` are the problem precisely because they are *not* neutral
metadata: two different serializers read them and reach different conclusions, while the third
ignores them. Removing them takes the ambiguity out rather than trying to keep three
interpretations aligned.

### Declaring a serializable type

```csharp
[MessagePackObject]
public sealed partial record SomeType(
    [property: Key(0)] ChatId ChatId,
    [property: Key(1)] long Version);
```

- **Include** — `[Key(N)]`. MessagePack's analyzer (`MsgPack004`) fails the build if a public
  member of a `[MessagePackObject]` type has neither `[Key]` nor `[IgnoreMember]`, so coverage
  is compiler-enforced. Newtonsoft and STJ need nothing: with no `[DataContract]`, both
  serialize all public properties by default.
- **Exclude** — all three, every time:

  ```csharp
  [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
  public ChatId ShardKey => ChatId;
  ```

  Omitting any one of the three leaks the member into that serializer. `[IgnoreDataMember]`
  is not a substitute for `[JsonIgnore]` — see finding 3.

- **MemoryPack** — add `[MemoryPackable]` / `[MemoryPackOrder]` **only** for types whose bytes
  already exist in the database (Flows and their reachable state, the `StoredSettings` union,
  the legacy `Language` formatters). New types never get them.

This is why the existing four-attribute idiom
`[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]` (142 occurrences)
is correct today and becomes a clean three once `[DataContract]` is gone.

## Migrating off `[DataContract]`

Current surface in `src/dotnet/`: ~505 `[DataContract]`, ~1977 `[DataMember]`,
449 `[MessagePackObject]`, 1417 `[Key(`.

Removing `[DataContract]` + `[DataMember]` from a type that already has
`[MessagePackObject]` + `[Key]`:

- **MessagePack** — no change (finding 2).
- **System.Text.Json** — no change; it never honored them.
- **Newtonsoft.Json** — **this is the one that changes.** The type flips from opt-in to
  opt-out, so property order changes to declaration order, and any public property that
  was excluded *only* by virtue of lacking `[DataMember]` starts being serialized.

So the migration's real work is per-type: every public member that must stay out of the
operation log needs an explicit `[Newtonsoft.Json.JsonIgnore]` before `[DataContract]` comes
off. Members that already carry `[IgnoreDataMember]` remain excluded (Newtonsoft honors it
without `[DataContract]`), but the convention above replaces it with the explicit trio anyway.

Six types currently override the opt-in behavior by hand and can drop the override along
with `[DataContract]`:

`UserIdentity`, `Choice<T,TAlt>`, `Range<T>`, `Tile<T>`, `Maybe<T>`, `Device` — all carry
`[Newtonsoft.Json.JsonObject(MemberSerialization.OptOut)]`.

## Testing

Two complementary helpers, both in `tests/Testing/`:

**`AssertPassesThroughSerializers`** (`SerializationTestExt`) round-trips a value through every
serializer and checks it comes back equal. This is necessary but not sufficient: a member that
one serializer emits and another silently drops still round-trips fine on the serializer that
keeps it, so round-trip equality cannot see a shape divergence.

**`AssertSameShapeAcrossSerializers`** (`SerializationShapeTestExt`) closes that gap. It
serializes once per serializer — using `MessagePackSerializer.ConvertToJson` to bring the binary
output into comparable form — and asserts:

1. Both JSON serializers agree on the **member names** (compared case-insensitively, since STJ
   is camelCase and Newtonsoft is PascalCase).
2. Both JSON serializers agree on the **scalar values** emitted.
3. MessagePack emits the same members — by name when the type is keyed by property name, or by
   slot count when it is integer-keyed.
4. MessagePack emits the same **number** of scalar values as JSON.

Give every member a distinct value in the test instance: the value checks are only as sharp as
the values are unique.

Two deliberate tolerances, both established by running the helper against real types:

- **Values are compared by count, not equality, against MessagePack.** Some scalars are encoded
  differently by design — a `Moment` is `"2026-08-04T09:09:07Z"` in JSON but `17858345477089040`
  in MessagePack. Counting tolerates that while still catching a dropped member.
- **Only top-level scalars are compared.** A nested value may legitimately differ in shape:
  `ApiOption<T>` is `{"hasValue":false,...}` in JSON but `[]` in MessagePack.

The Newtonsoft leg runs with `NullValueHandling.Include`, unlike production, so a member that
exists but is currently null still shows up as part of the type's shape.

### What it caught

Pointed at the codebase before the ignore sweep, it flagged real divergences immediately.
`FlowTypeStat` was typical:

```
SystemJson:  {"name":"test","completed":1,...,"idle":5,"total":15,"problematic":6}
Newtonsoft:  {"Name":"test","Completed":1,...,"Idle":5}
MessagePack: ["test",1,2,3,4,5]
```

`Total` and `Problematic` are computed properties that carried `[IgnoreDataMember, IgnoreMember]`
but no `[JsonIgnore]`, so System.Text.Json emitted them and the other two didn't. Both now carry
the full trio, as does every other member that was in that state.

### The wire-format baseline

`WireFormatBaselineTest` guards the format across a refactor that is meant to be format-neutral.
It enumerates every `[MessagePackObject]` type, builds a deterministic instance of each via
`TestValueBuilder`, and writes four files per type: the MessagePack bytes, those bytes rendered
as JSON, and each JSON serializer's output.

Generation is gated on `ActualChat_WriteWireFormatBaseline=true` and the output is gitignored:
it is produced locally from the pre-refactor build, and its absence just means "nothing to guard
yet", so CI stays green without it. Verification rebuilds each value, re-serializes it, and
asserts the MessagePack bytes are **identical** — sound because two independent generations
produce byte-identical output across all 354 types. JSON differences are reported but not
asserted, since dropping `[DataContract]` moves Newtonsoft's output by design.

## Related

- [`docs/plans/msgpack.md`](../plans/msgpack.md) — the MemoryPack → MessagePack migration
  plan. Phases 1–3 are complete; phase 4 (removing `[DataContract]`) is next.
- [`docs/native-aot.md`](../native-aot.md) — why the AOT resolver chain excludes dynamic
  IL emit, which is what makes `[MessagePackObject]` mandatory rather than optional.
