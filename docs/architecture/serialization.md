# Serialization

Voxt serializes the same types through several serializers at once. This page describes
which serializer owns which path, what each one actually honors, and the attribute
convention that follows from that.

Everything in the "What each serializer honors" table was verified empirically against
this repository's serializer configuration, not taken from upstream documentation.

**The short version.** Three serializers are live — Newtonsoft.Json, System.Text.Json,
MessagePack — and every serializable type must work in all three. Each serializer is marked up
with **its own** attributes: `[DataContract]`/`[DataMember]` are Newtonsoft's,
`[MessagePackObject]`/`[Key]` are MessagePack's, `[JsonIgnore]` is System.Text.Json's. MemoryPack
is not a serialization target at all anymore; it survives only as a read leg for two kinds of
already-persisted blob — legacy stored settings and legacy flow state. See
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
  `[DataContract]`**, which is what makes it Newtonsoft's markup rather than anyone else's

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
  (`UserPttSettingsTest`, `StoredSettingsSerializationTest`).
- **Flows** — `FlowData.FlowSerializer` is a `VersionedByteSerializer` whose format 0 is
  `Serializers.MessagePackTypeDecorating`, with `Serializers.MemoryPackTypeDecorating` as the
  `legacy:` leg for flow state persisted before the format byte existed.

#### The retained closure

66 types still carry MemoryPack markup, one per file, all reachable from those two paths:

| Group | Types |
|---|---|
| Flows | every pre-cutoff `Flow` subclass and the state reachable from it, plus `FlowId`, `FlowData<TFlow>`, `FlowReadiness`, `IndexingFlowCursor` |
| KVAS | the `StoredSettings` union base and 20 of its members — `User*Settings`, `ChatUserSettings`, `ChatInviteSettings`, `ChatListSettings`, `LocalAppSettings`, `LocalOnboardingSettings`, … |
| Reachable value types | `Range<T>`, `MediaRef`, `ChatEntrySlim`, `HashedExternalContact` |
| Identifiers | the 10 id types those reach — `[MemoryPackable(GenerateType.NoGenerate)]` plus a hand-written formatter registered in `ApiModuleInitializer`, including the legacy `Language` formatters |

The identifier list is the sharpest test of the "reachable from" rule: an id type gets MemoryPack
markup **and** a `MemoryPackFormatterProvider.Register` call in `ApiModuleInitializer`, or
neither. `TranscriberId` had the attribute but no registration — dead markup, since nothing could
ever have serialized it through MemoryPack.

Note that KVAS blobs are not only server-side: `LocalAppSettings` and `LocalOnboardingSettings`
live in client-local storage, so the read leg has to survive on devices too.

#### The rule

> **Only legacy stored settings and legacy flow state need MemoryPack.** Those are the two read
> paths, and nothing else. If some other type carries `[MemoryPackable]`, the only legitimate
> reason is that it is *reachable from* a settings type or a flow — a member's type, an element
> type, an identifier one of them stores.

Never add `[MemoryPackable]`, `[MemoryPackOrder]`, `[MemoryPackUnion]`, or `[MemoryPackIgnore]`
to anything new.

##### The cutoff: when the MessagePack write path shipped

A type needs MemoryPack only if a **released** build could have written a MemoryPack blob of it.
`bcfbc3f750` (2026-04-16) made `KvasSerializer.Write` unconditionally MessagePack and put
MessagePack in front of the Flow serializer, but the dev merge date isn't the boundary — release
branches are cut from dev and can take cherry-picks, so what matters is the first release that
carried the change:

| Release | Cut | MessagePack write path |
|---|---|---|
| `release/v2.7` | 2026-04-10 | no — the last MemoryPack-writing release |
| `release/v2.8` | 2026-05-18 | yes — the first one with it |

So **a type introduced after 2026-05-18 (plus a few days' margin) cannot have MemoryPack blobs
anywhere** and must be MessagePack-only. Check with
`git log --follow --diff-filter=A --format=%ad --date=short -- <file>`, and use the date the
type reached `dev`, not the date its branch started.

Two flows sit inside that margin and keep their markup deliberately:
`ChatEntryContentIndexingFlow` and `ChatMediaIndexingFlow`, both 2026-05-21. They are three days
past the v2.8 cut and cost nothing to leave as they are, so they stay rather than being trimmed
on a technicality.

##### Adding a member to a type already inside the closure

This is the one case where you *do* write a new `[MemoryPackOrder]`: the type's existing blobs
must keep deserializing, so its MemoryPack numbering has to continue from where they left off.
That numbering is independent of the `[Key]` numbering and the two have already drifted apart
on older types (`ChatUserSettings` pairs `MemoryPackOrder(3)` with `Key(2)`), so append to each
sequence separately rather than assuming they match.

Either way, a member added later is absent from older blobs and deserializes as `default(T)` —
a property initializer does not survive the gap. That is why `UserPttSettings.IsHeadsetButtonEnabled`
is `bool?` and read as `?? true` rather than a `bool ... = true`. This applies to MessagePack
just as much as to MemoryPack, so it holds for post-cutoff types too.

##### New types

A brand-new type has no legacy blobs by definition, so it gets MessagePack markup only —
including a new `StoredSettings` member, which needs `[Union(N, …)]` and no `[MemoryPackUnion]`.
`StoredSettings` shows both shapes: it declares 23 `[Union]` members but only 20
`[MemoryPackUnion]` ones, because `RecentGifs`, `RecentMentions`, and `UserPttSettings`
are MessagePack-only.

#### What unblocks full removal

The last two steps of the MemoryPack removal — dropping the NuGet packages and the
`MemoryPack` global using — stay open until the persisted blobs are migrated or aged out.
Concretely: every `0x0`-marked KVAS value rewritten as `0x1`, every pre-format-byte Flow row
re-serialized, and no installed client still holding local settings written by a pre-v2.8
build.

What remains carries MemoryPack purely as a read leg: the Flow types and their reachable
state, the `StoredSettings` union, the legacy `Language` formatters, `MediaRef`, `Range<T>`,
`ChatEntrySlim`, and `HashedExternalContact`.

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
> up with its own attributes.** No attribute is expected to mean the same thing to two of them.

| Serializer | Type | Include member | Exclude member |
|---|---|---|---|
| **Newtonsoft.Json** | `[DataContract]` | `[DataMember(Order = N)]` | `[IgnoreDataMember]` / `[Newtonsoft.Json.JsonIgnore]` |
| **System.Text.Json** | — | — (every public property) | `[JsonIgnore]` |
| **MessagePack** | `[MessagePackObject]` | `[Key(N)]` | `[IgnoreMember]` |
| **MemoryPack** | `[MemoryPackable]` | `[MemoryPackOrder(N)]` | `[MemoryPackIgnore]` — legacy blobs only |

`[DataContract]`/`[DataMember]` are **Newtonsoft's** markup. They are the one place this scheme
could go wrong, because MessagePack's *dynamic* resolver also reads them — but only for a type
without `[MessagePackObject]`. Where `[MessagePackObject]` + `[Key]` is present, `[Key]` wins and
the DataContract annotations have no effect on the wire format at all (finding 2, verified
byte-identical). That is what keeps them unambiguous, and it is why the next rule is not
optional.

> **A `[DataContract]` type must also be `[MessagePackObject]`.** Never one without the other.
> Otherwise MessagePack falls back to reading the DataContract annotations — the ambiguous
> case — and that path needs dynamic IL emit, so it doesn't work under AOT either.

### Declaring a serializable type

```csharp
[DataContract, MessagePackObject]
public sealed partial record SomeType(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long Version);
```

- **Type** — `[DataContract, MessagePackObject]`. Under AOT the dynamic resolver is unavailable,
  so `[MessagePackObject]` is mandatory rather than optional; see
  [`docs/native-aot.md`](../native-aot.md). `[MessagePackObject(true)]` keys by property name
  instead of by integer slot — use it where a stable name is worth more than compactness.
  System.Text.Json needs no type-level attribute.
- **Include** — `[DataMember, Key(N)]`. MessagePack's analyzer (`MsgPack004`) fails the build if
  a public member of a `[MessagePackObject]` type has neither `[Key]` nor `[IgnoreMember]`, so
  MessagePack coverage is compiler-enforced. **`[DataMember]` is not** — and on a
  `[DataContract]` type a member without it is silently dropped from the operation log
  (finding 1). That one is on you.
- **`[Key]` ordinals are wire format** — never renumber or reuse one on a type that has already
  shipped; append instead. `[DataMember(Order = N)]` only controls Newtonsoft's output order and
  may be omitted.
- **Exclude** — all four, every time:

  ```csharp
  [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
  public ChatId ShardKey => ChatId;
  ```

  Omitting any one leaks the member into that serializer. `[IgnoreDataMember]` is not a
  substitute for `[JsonIgnore]` — see finding 3. On a `[DataContract]` type the Newtonsoft pair
  is belt-and-braces (opt-in already excludes an unmarked member), but writing all four keeps the
  intent explicit and survives the type later losing `[DataContract]`.

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
| Serializable type | `[DataContract, MessagePackObject]` |
| Serializable type, name-keyed | `[DataContract, MessagePackObject(true)]` |
| Include a member | `[DataMember, Key(N)]` — or `[property: DataMember, Key(N)]` in a primary constructor |
| Exclude a member | `[JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]` |
| Polymorphic base | `[MessagePackObject]` + `[Union(N, typeof(TDerived))]` per subtype |
| Pick a constructor | `[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]` |
| Legacy settings / flow blob | add `[MemoryPackable]` + `[MemoryPackOrder(N)]` — nothing else, ever |

Current surface in `src/dotnet/`, counted at the time of writing:

| | `[DataContract]` | `[DataMember]` | `[IgnoreDataMember]` | `[MessagePackObject]` | `[Key(N)]` | `[IgnoreMember]` |
|---|---|---|---|---|---|---|
| Occurrences | 534 | 1632 | 422 | 474 | 1461 | 343 |

The dominant idiom in the tree is `[DataContract, MessagePackObject]` (367 types) with
`[property: DataMember, Key(N)]` members (651) — which is exactly the convention above.

## If you ever do remove `[DataContract]` from a type

Not a goal, and not a drive-by cleanup — but if there's a reason to, know what moves:

- **MessagePack** — no change (finding 2), provided `[MessagePackObject]` + `[Key]` stay.
- **System.Text.Json** — no change; it never honored them.
- **Newtonsoft.Json** — **this is the one that changes.** The type flips from opt-in to
  opt-out, so property order changes to declaration order, and any public property that
  was excluded *only* by virtue of lacking `[DataMember]` starts being serialized — silently, and
  into the operation log if it's reachable from a backend command.

So every public member that must stay out needs an explicit `[Newtonsoft.Json.JsonIgnore]` first.
Members already carrying `[IgnoreDataMember]` stay excluded — Newtonsoft honors it without
`[DataContract]`.

Six types override the opt-in behavior by hand and would drop the override along with
`[DataContract]`: `UserIdentity`, `Choice<T,TAlt>`, `Range<T>`, `Tile<T>`, `Maybe<T>`, `Device` —
all carry `[Newtonsoft.Json.JsonObject(MemberSerialization.OptOut)]`.

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
asserted, since a change to a type's Newtonsoft markup may move that output by design.

## Related

- [`docs/CODING_STYLE.md` → Serialization Attributes](../CODING_STYLE.md#serialization-attributes)
  — the short rule list, for when you just need to know what to type.
- [`docs/native-aot.md`](../native-aot.md) — why the AOT resolver chain excludes dynamic
  IL emit, which is what makes `[MessagePackObject]` mandatory rather than optional.
