# Serialization

Voxt serializes the same types through several serializers at once. This page describes
which serializer owns which path, what each one actually honors, and the attribute
convention that follows from that.

Everything in the "What each serializer honors" table was verified empirically against
this repository's serializer configuration, not taken from upstream documentation.

**The short version.** Three serializers are live — Newtonsoft.Json, System.Text.Json,
MessagePack — and every serializable type must work in all three. MemoryPack is not a
serialization target anymore; it survives only as a read leg for bytes already persisted.
Markup is always the owning serializer's own attributes: `System.Runtime.Serialization`
attributes are being removed, because two of the three serializers read them and reach
different conclusions while the third ignores them. See
[Attribute conventions](#attribute-conventions).

## The serializers and what each one owns

| Serializer | Owns | Authoritative for |
|---|---|---|
| **Newtonsoft.Json** | Operation log and event log | `DbOperation.CommandJson`, `DbOperation.ItemsJson`, `DbEvent` |
| **System.Text.Json** | Blazor → JavaScript interop | Everything crossing the JS boundary |
| **MessagePack** | RPC wire format, KVAS, Flow state | All RPC payloads; everything binary that is *written* |
| **MemoryPack** | Nothing — read-only legacy leg | Decoding KVAS and Flow blobs written by earlier versions |

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

### MessagePack — every binary path

The only binary format **written** today: RPC payloads, KVAS values, and Flow state all go
out as MessagePack. `CoreModuleInitializer` registers six client formats — `msgpack6`,
`msgpack6c`, and an LZ4 / LZ4-frame variant of each (`msgpack6-lz4`, `msgpack6c-lz4`,
`msgpack6-lz4f`, `msgpack6c-lz4f`) — and the server adds `json5`, `json5np`, plus the
keyless `msgpack6k` / `msgpack6ck`. The negotiated default is `msgpack6` on the server,
`msgpack6c-lz4` in WASM (no outbound compression there), and `msgpack6c-lz4f` on every other
client; `DEBUG` builds pin plain `msgpack6`.

`Serializers.MessagePack` is also `ByteSerializer.Default`, so anything that reaches for the
ambient binary serializer gets MessagePack.

### MemoryPack — a read-only legacy leg

**MemoryPack is no longer a serialization target.** It was removed from every path that
writes new bytes; what remains decodes blobs that earlier app versions already wrote.

What the removal took out (`7012092087`, *drop MemoryPack from everything but flows and
KVAS*):

- the `mempack5` / `mempack6` RPC formats — RPC negotiates MessagePack only;
- `[MemoryPackable]` / `[MemoryPackOrder]` on every API contract, command, event, and domain
  model outside the retained closure below;
- the `ApiNullable8<T>` bridges — private 8-byte shadow properties that existed solely so
  MemoryPack could carry a `Nullable<T>` (`ChatEntry.MemoryPackEndsAt`,
  `Upload.MemoryPackLength`, `Device.MemoryPackAccessedAt`, …);
- most `StringLikeMemoryPackFormatter` registrations;
- the per-type MemoryPack `CodeKeeper` keeps. `Directory.Build.props` now sets the
  `MemoryPackByteSerializer.IsEnabled` feature switch to `false` with `Trim="true"`, so the
  generic serializer is trimmed away and only the non-generic
  `MemoryPackByteSerializer.Default` survives.

Two read paths remain, and neither of them writes:

- **KVAS** — `KvasSerializer` dispatches on a leading marker byte: `0x0` → MemoryPack,
  `0x1` → MessagePack, anything else → legacy unmarked JSON. `Write` always emits `0x1`.
  The `PreferMemoryPack` switch that flips it is **test-only** — it exists so tests can
  manufacture a legacy-shaped blob and prove the read leg still decodes it
  (`UserWalkieTalkieSettingsTest`, `StoredSettingsSerializationTest`).
- **Flows** — `FlowData.FlowSerializer` is a `VersionedByteSerializer` whose format 0 is
  `Serializers.MessagePackTypeDecorating`, with `Serializers.MemoryPackTypeDecorating` as the
  `legacy:` leg for flow state persisted before the format byte existed.

#### The retained closure

74 types still carry MemoryPack markup, one per file, all reachable from those two paths:

| Group | Types |
|---|---|
| Flows | every `Flow` subclass and the state reachable from it, plus `FlowId`, `FlowData<TFlow>`, `FlowReadiness`, `IndexingFlowCursor` |
| KVAS | the `StoredSettings` union base and 21 of its members — `User*Settings`, `ChatUserSettings`, `ChatInviteSettings`, `ChatListSettings`, `LocalAppSettings`, `LocalOnboardingSettings`, … |
| Reachable value types | `Range<T>`, `MediaRef`, `ChatEntrySlim`, `TranscriptionContext` and friends, `HashedExternalContact` |
| Identifiers | the 11 id types those reach — `[MemoryPackable(GenerateType.NoGenerate)]` plus a custom formatter, including the legacy `Language` formatters |

Note that KVAS blobs are not only server-side: `LocalAppSettings` and `LocalOnboardingSettings`
live in client-local storage, so the read leg has to survive on devices too.

#### The rule

> **MemoryPack attributes are allowed only on a type whose bytes are already persisted
> somewhere.** Never add `[MemoryPackable]`, `[MemoryPackOrder]`, `[MemoryPackUnion]`, or
> `[MemoryPackIgnore]` to anything new.

The one case where you *do* write a new `[MemoryPackOrder]` is **adding a member to a type
already inside the closure** — its existing blobs must keep deserializing, so the MemoryPack
numbering has to continue from where they left off. That numbering is independent of the
`[Key]` numbering and the two have already drifted apart on older types (`ChatUserSettings`
pairs `MemoryPackOrder(3)` with `Key(2)`), so append to each sequence separately rather than
assuming they match.

`UserWalkieTalkieSettings.IsHeadsetButtonEnabled` is the worked example: added as
`[MemoryPackOrder(8), Key(8)]`, and typed `bool?` rather than `bool` precisely because a
member absent from an older blob deserializes as `default(T)` — a `bool ... = true` property
initializer does not survive the gap.

A brand-new type has no legacy blobs by definition, so it gets MessagePack markup only —
including a new `StoredSettings` member, which needs `[Union(N, …)]` and no `[MemoryPackUnion]`.
`StoredSettings` already shows both shapes: it declares 23 `[Union]` members but only 21
`[MemoryPackUnion]` ones — `RecentGifs` and `RecentMentions` were added MessagePack-only and
work fine that way.

#### What unblocks full removal

Steps 2 and 3 of [phase 3](../plans/msgpack.md#phase-3-remove-memorypack-complete) — dropping
the NuGet packages and the `MemoryPack` global using — stay open until the persisted blobs are
migrated or aged out. Concretely: every `0x0`-marked KVAS value rewritten as `0x1`, every
pre-format-byte Flow row re-serialized, and no installed client still holding local settings
written by an older build.

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

## Attribute conventions

> **Everything serializable supports all three live serializers, and each serializer is marked
> up with its own attributes. `System.Runtime.Serialization` attributes — `[DataContract]`,
> `[DataMember]`, `[IgnoreDataMember]` — are not used.**

`[DataContract]`/`[DataMember]` are the problem precisely because they are *not* neutral
metadata: two different serializers read them and reach different conclusions, while the third
ignores them. Removing them takes the ambiguity out rather than trying to keep three
interpretations aligned. Note that `System.Runtime.Serialization` is a **global using**
(`Directory.Build.props`), so these attributes are always one careless keystroke away —
there is no missing-`using` error to catch them.

### Declaring a serializable type

```csharp
[MessagePackObject]
public sealed partial record SomeType(
    [property: Key(0)] ChatId ChatId,
    [property: Key(1)] long Version);
```

- **Type** — `[MessagePackObject]`. Under AOT the dynamic resolver is unavailable, so this is
  mandatory rather than optional; see [`docs/native-aot.md`](../native-aot.md).
  `[MessagePackObject(true)]` keys by property name instead of by integer slot — use it where
  a stable name is worth more than compactness. Newtonsoft and STJ need no type-level
  attribute: with no `[DataContract]`, both serialize all public properties by default.
- **Include** — `[Key(N)]`. MessagePack's analyzer (`MsgPack004`) fails the build if a public
  member of a `[MessagePackObject]` type has neither `[Key]` nor `[IgnoreMember]`, so coverage
  is compiler-enforced. **`[Key]` ordinals are wire format** — never renumber or reuse one on
  a type that has already shipped; append instead.
- **Exclude** — all three, every time:

  ```csharp
  [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
  public ChatId ShardKey => ChatId;
  ```

  Omitting any one of the three leaks the member into that serializer. `[IgnoreDataMember]`
  is not a substitute for `[JsonIgnore]` — see finding 3. This matters more without
  `[DataContract]`, not less: Newtonsoft is then in opt-out mode, so an unmarked computed
  property goes into the operation log.

  Unqualified `JsonIgnore` is System.Text.Json's; Newtonsoft's must always be written out in
  full. Both are needed — they are different attributes.
- **Unions** (polymorphic payloads) — `[Union(N, typeof(TDerived))]` on the base, alongside
  `[MessagePackObject]`. Same rule as `[Key]`: the tags are wire format, so append rather than
  renumber.
- **Constructors** — a record whose primary constructor doesn't cover every serialized member
  needs the deserializer pointed at the right one:
  `[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]`.
  `SerializationConstructor` is MessagePack's; the two `JsonConstructor`s are STJ's and
  Newtonsoft's.
- **MemoryPack** — never, on anything new. See [the rule](#the-rule) above for the one
  exception.

### Backend commands have a fourth requirement

The operation log is Newtonsoft-serialized, so **every non-delegating backend command, and
every type reachable from one, must round-trip through Newtonsoft.Json.** An API command that
only delegates to a backend command is never persisted and carries no such requirement.

### Cheat sheet

| Intent | Write |
|---|---|
| Serializable type | `[MessagePackObject]` |
| Serializable type, name-keyed | `[MessagePackObject(true)]` |
| Include a member | `[Key(N)]` — or `[property: Key(N)]` in a primary constructor |
| Exclude a member | `[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]` |
| Polymorphic base | `[MessagePackObject]` + `[Union(N, typeof(TDerived))]` per subtype |
| Pick a constructor | `[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]` |
| Anything from `System.Runtime.Serialization` | — don't |

## Migrating off `[DataContract]`

**The convention above is in force for new code; the sweep over existing code has not run yet.**
Current surface in `src/dotnet/`, counted at the time of writing:

| | `[DataContract]` | `[DataMember]` | `[IgnoreDataMember]` | `[MessagePackObject]` | `[Key(N)]` | `[IgnoreMember]` |
|---|---|---|---|---|---|---|
| Occurrences | 534 | 1632 | 422 | 474 | 1461 | 343 |

So the dominant idiom you will actually see in the tree is still `[DataContract, MessagePackObject]`
(367 types) with `[property: DataMember, Key(N)]` members (651). That is expected: MessagePack
support was added *alongside* the DataContract markup rather than replacing it, and finding 2
is what makes leaving it there harmless in the meantime. Don't copy it into a new type, and
don't strip it from an existing one as a drive-by — see the hazard below.

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

It mirrors ActualLab.Testing's serializer matrix **minus MemoryPack** — a general serializable
type is not expected to be MemoryPack-able anymore. The legacy read leg is covered where it
actually exists instead: `FlowSerializationTestBase.MemoryPack_RoundTrip` runs only when
`SerializationCodeGen.IsMemoryPackable(typeof(TFlow))` and skips otherwise, and the KVAS
settings tests write through a `PreferMemoryPack = true` serializer to produce legacy bytes on
purpose.

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

- [`docs/CODING_STYLE.md` → Serialization Attributes](../CODING_STYLE.md#serialization-attributes)
  — the short rule list, for when you just need to know what to type.
- [`docs/plans/msgpack.md`](../plans/msgpack.md) — the MemoryPack → MessagePack migration
  plan. Phases 1–3 are complete; phase 4 (removing `[DataContract]`) is next.
- [`docs/native-aot.md`](../native-aot.md) — why the AOT resolver chain excludes dynamic
  IL emit, which is what makes `[MessagePackObject]` mandatory rather than optional.
