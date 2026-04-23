# Nerdbank.MessagePack

Notes on how the project uses [Nerdbank.MessagePack](https://aarnott.github.io/Nerdbank.MessagePack/)
as the post-migration default binary serializer. Pairs with
[PolyType](https://eiriktsarpalis.github.io/PolyType/) for shape generation.

## Wire format basics

Two distinct shapes per type:

| Property attributes | Wire | Renames safe? |
|---|---|---|
| Default (no `[Key]`) | name-keyed map: `{"PropA": ..., "PropB": ...}` | No — renames break the wire |
| `[Key(N)]` on every serialized member | positional array: `[valN0, valN1, …]` | Yes — names don't ride the wire |

`[Key]` is **all-or-nothing per type**: if any serializable member has it, every other serializable member must too. The analyzer that enforces this is [NBMsgPack001](https://aarnott.github.io/Nerdbank.MessagePack/analyzers/NBMsgPack001.html). Each index must be unique within an inheritance chain — [NBMsgPack003](https://aarnott.github.io/Nerdbank.MessagePack/analyzers/NBMsgPack003.html).

In this project we put `[Key(N)]` on every `[MemoryPackable]` DTO so .NET-to-.NET RPC rides the rename-safe positional-array wire. Serializer-build flips the switch at runtime to also expose a **keyless** variant (`IgnoreKeyAttributes = true` on the underlying `MessagePackSerializer`) used by JS peers that natively encode objects as name-keyed maps — see *RPC formats* below.

The `NbKey` alias (declared in the root `Directory.Build.props`) maps to `Nerdbank.MessagePack.KeyAttribute` so server projects (which alias `KeyAttribute` to `System.ComponentModel.DataAnnotations.KeyAttribute` for EF) can write `[NbKey(N)]` without ambiguity. Both `[Key(N)]` and `[NbKey(N)]` resolve to the same Nerdbank attribute — pick whichever reads better in context.

## Customizing the shape — PolyType attributes

PolyType controls *what* Nerdbank sees. We rely on three attributes:

- **`[PropertyShape(Ignore = true)]`** — exclude a property from the shape. PolyType's reflection provider does respect `[IgnoreDataMember]` at runtime, but the NBMsgPack001 analyzer doesn't trust that consistently — apply `[PropertyShape(Ignore = true)]` explicitly when you need it to stay quiet (or for any computed/projected member that shouldn't survive deserialization).

- **`[ConstructorShape]`** — pick *the* constructor PolyType should use for deserialization. Without it PolyType walks every ctor and may pick a self-referencing one (e.g. `Foo(Foo? other)`) which trips the converter cache with `"The type 'Foo' has a delayed value that has not been completed"`. We pair it with every `[MemoryPackConstructor]` in the codebase.

- **`[PropertyShape(Name = "…")]`** — rename a property on the wire (only meaningful for the map-keyed shape, since `[Key(N)]` already detaches names from the wire).

See the [customizing-serialization docs](https://aarnott.github.io/Nerdbank.MessagePack/docs/customizing-serialization.html).

## Inheritance

Each `[Key]` index must be unique across the entire inheritance chain. Our convention: pick a generous reserved range for the base, derived types start at `base_max + 1`. Current reservations:

| Base type | Reserved range | Derived offset |
|---|---|---|
| `Account`, `Author`, `Avatar`, `Media`, `MediaFrame` | 0–9 | starts at 10 |
| `LiveStreamItem` | 0–2 | starts at 3 |
| `ExternalContact` | 0–4 | starts at 5 |
| `StoredSettings` | none | each derivative starts at 0 |

For overrides:
- **Override of a concrete base member** — re-emit the same `[Key(N)]` value as the base. The analyzer treats them as one member.
- **Override of an abstract base member that's `[PropertyShape(Ignore = true)]`** — the abstract slot is hidden from the base shape, the override gets a fresh Key (we use the conceptual base index — e.g. `MediaFrame.Offset` is abstract+ignored, `AudioFrame.Offset` re-emerges as `Key(1)`).
- **`MediaFrame` override quirk** — when an override re-emerges, you MUST re-attribute it with `[PropertyShape]` (no `Ignore`) on the *override*. PolyType inherits `Ignore = true` from the base; without an explicit `[PropertyShape]` on the derived member, the property is silently dropped from the wire and frames round-trip with default `Offset`/`Duration`/`IsKeyFrame`. See `Api/Media/MediaFrame.cs`, `Api/Audio/AudioFrame.cs`, `Api/Video/VideoFrame.cs`.

## Unions (polymorphism)

[Nerdbank's union docs](https://aarnott.github.io/Nerdbank.MessagePack/docs/unions.html). Two ways:

- **`[DerivedTypeShape(typeof(Derived), Tag = N)]` on the base** — PolyType-native, no custom converter. **What we use.** Wire: `[N, payload]` (2-element array with int tag). Same shape MessagePack-CSharp's `[Union(N, type)]` produced.
- **`UseDiscriminatorObjects = true`** on the serializer — switches to `{N: payload}` (1-key map). **We don't use this** — it breaks wire-compat with the MP-CSharp era and isn't required for any TS interop.

We keep the legacy `[MemoryPackUnion]` attributes alongside `[DerivedTypeShape]` (same Tag values) so MemoryPack still dispatches the same hierarchies. Bases this applies to: `MediaFrame`, `LiveStreamItem`, `StoredSettings`, `IFileProvider` (interface unions work via `[DerivedTypeShape]` too).

## AOT — PolyType witnesses

`[GenerateShape]` on a `partial` type makes it implement `IShapeable<Self>`. `[GenerateShapeFor<T>]` on a partial witness class makes it implement `IShapeable<T>` for any `T` (works cross-assembly).

PolyType source-gen is opt-in per project — gated by `<UsePolyTypeGenerator>true</UsePolyTypeGenerator>` in the root `Directory.Build.props`. Five projects are flagged today: `Core`, `Api`, `Api.Contracts`, `UI.Blazor`, `UI.Blazor.App`. Adding a new project that needs the source-generator means flipping the flag for it.

For each flagged project, `App.AotHelper -g` emits two checked-in files under `Module/`:

- **`{Project}ModuleInitializer.g.cs`** — the witness class plus a `[ModuleInitializer] RegisterGenerated()` half. The witness has one `[GenerateShapeFor<T>]` line per serializable type in the assembly; the generated `RegisterGenerated()` calls `Serializers.RegisterShapeProvider({Witness}.GeneratedTypeShapeProvider)` and `AotTypes.AddSource(new {Project}AotSource())`.
- **`{Project}AotSource.g.cs`** — exposes the project's `(Type, AotTypeKind)[]` list to `AotTypes` (used by trimming-readiness tests and the AOT code-keeper).

In addition, hand-written `src/dotnet/Core/Serialization/FrameworkWitness.cs` carries `[GenerateShapeFor<T>]` for framework helper types Fusion needs but doesn't ship a witness for. Its provider is registered explicitly in the hand-written `CoreModuleInitializer.ModuleInitializer`.

Non-AOT runs work without the generated witnesses — they're an ILC-retention concern.

`AotTypes.KeepSerializable<T, TWitness>()` (in `src/dotnet/Core/Aot/AotTypes.cs`) is the per-type keep helper:

1. Calls `CodeKeeper.Keep<T>()` (preserves `T`'s metadata).
2. Calls `CodeKeeper.Keep<NerdbankMessagePackByteSerializer<T>>()` — the closed generic that `MakeGenericType` would build at runtime.
3. Pretend-invokes `MessagePackSerializer.Serialize<T, TWitness>` / `Deserialize<T, TWitness>` under the dead `if (CodeKeeper.AlwaysFalse)` guard so ILC walks the path.

## Serializers facade

`src/dotnet/Core/Serialization/Serializers.cs` is the central serializer facade. It owns the lifecycle of two byte-serializer pairs (key-honoring + keyless), each with a plain and a type-decorating variant:

```csharp
Serializers.MessagePack                      // [Key]-honoring, server reflection-augmented
Serializers.MessagePackTypeDecorating
Serializers.KeylessMessagePack               // IgnoreKeyAttributes = true (name-keyed maps)
Serializers.KeylessMessagePackTypeDecorating

Serializers.ClientSide.MessagePack           // codegen-only, no reflection — what AOT uses
Serializers.ClientSide.KeylessMessagePack
// + the TypeDecorating variants on ClientSide
```

`Serializers.ClientSide` is the codegen-only state with **no** reflection fallback — exactly what Wasm / MAUI / NativeAOT see at runtime. Server code reads `Serializers.MessagePack` (reflection-augmented). `tests/Core.Server.IntegrationTests/AotFormatterPresenceTest.cs` walks every `AotTypeKind.Serializable` entry and asserts both `ClientSide.MessagePack` and `ClientSide.KeylessMessagePack` resolve a converter for it — catches "works on server, breaks on AOT" regressions before they ship.

Three registration calls extend the wire (each rebuilds both serializer states lazily):

```csharp
Serializers.RegisterShapeProvider(provider);             // a witness's GeneratedTypeShapeProvider
Serializers.RegisterStringLikeTypes(types);              // each gets a StringLikeNerdbankConverter<T>
Serializers.RegisterConverters(converters);              // one-offs, e.g. HostRoleNerdbankConverter
```

Polymorphic union dispatch comes from `[DerivedTypeShape]` attributes — no registration step needed.

The set of open generics that `Serializers.Build` injects on top of Fusion's defaults (in addition to anything Fusion already provides):

```
RangeNerdbankConverter<>            SetDiffNerdbankConverter<>           SetDiffNerdbankConverter<,>
ChangeNerdbankConverter<>           ChangeNerdbankConverter<,>           ExpiringNerdbankConverter<>
TrimmedNerdbankConverter<>          NullableNerdbankConverter<>          BoxNerdbankConverter<>
MaybeNerdbankConverter<>            ImmutableDictionaryNerdbankConverter<,>
```

Each open generic must be paired with a `[TypeShapeExtension(target, AssociatedTypes = [converter])]` declaration in `src/dotnet/Core/Serialization/SerializerAssociatedTypes.cs` — without it source-gen returns null from `GetAssociatedTypeShape` and Nerdbank can't resolve the converter (surfaces as the misleading `"delayed value that has not been completed"` cache error mid-graph-walk).

## Per-assembly registration

Each downstream module's hand-written `[ModuleInitializer]` calls the registry methods with what it owns:

```csharp
[ModuleInitializer]
internal static void ModuleInitializer()
{
    CoreModuleInitializer.Load();   // forces base init
    Serializers.RegisterStringLikeTypes(StringLikeIdentifiers);
    Serializers.RegisterConverters([
        CachingAudioFrameFormatter.Instance,
        CachingVideoFrameFormatter.Instance,
    ]);
    // …MemoryPack registrations, etc.
}
```

The generated `*ModuleInitializer.g.cs` half handles the witness + AOT source registration automatically (see *AOT* above) — the hand-written half only owns the pieces codegen can't derive: `StringLikeTypes`, custom converters, and any other lifecycle one-offs.

## CoreModuleInitializer.Configure — RPC formats

`src/dotnet/Core/Module/CoreModuleInitializer.cs` declares four wire formats:

```csharp
public static readonly RpcSerializationFormat MessagePackV6   = new("msgpack6",   …, RpcByteMessageSerializerV5);
public static readonly RpcSerializationFormat MessagePackV6C  = new("msgpack6c",  …, RpcByteMessageSerializerV5Compact);
public static readonly RpcSerializationFormat MessagePackV6K  = new("msgpack6k",  …, RpcByteMessageSerializerV5);
public static readonly RpcSerializationFormat MessagePackV6CK = new("msgpack6ck", …, RpcByteMessageSerializerV5Compact);
```

| Key | Argument serializer | Envelope | Used by |
|---|---|---|---|
| `msgpack6` | `Serializers.MessagePack` (Key-honoring) | V5 | server↔server, .NET client default in DEBUG |
| `msgpack6c` | `Serializers.MessagePack` | V5 Compact | .NET client default in Release |
| `msgpack6k` | `Serializers.KeylessMessagePack` | V5 | (registered, available for negotiation) |
| `msgpack6ck` | `Serializers.KeylessMessagePack` | V5 Compact | **TS clients** |

`Configure()` (called from app startup + the test bootstrap) replaces `RpcSerializationFormat.All` with the right list per side: server registers all four MessagePack variants plus the legacy MemoryPack/JSON formats; client (non-server) registers only `MessagePackV6` and `MessagePackV6C` because that's all `RpcSerializationFormatResolver.Default` needs to negotiate. It also clears `DefaultFormats = null!` so the resolver recomputes its format set.

System RPC types (`RpcHandshake`, `Result`, `ExceptionInfo`, …) keep their array wire under both variants because their explicit converters take precedence over the `IgnoreKeyAttributes` toggle — the keyless flag only affects PolyType-emitted converters for application types.

`Configure()` also forces `_ = Serializers.MessagePack` to materialize early, so any code that captures the framework statics (`ByteSerializer.Default`, `NerdbankMessagePackByteSerializer.Default*`) at module-init time sees the project's tuned serializer rather than Fusion's stock defaults.

## TS-side wiring

`src/nodejs/src/api/api.ts` is where TS-side RPC bootstrap lives. It registers the keyless formats at module load (Fusion only ships `msgpack6` / `msgpack6c`) and rebuilds the resolver so the new keys are visible:

```ts
const SERIALIZATION_FORMAT = 'msgpack6ck';

(RpcSerializationFormat.All as RpcSerializationFormat[]).push(
    new RpcMessagePackSerializationFormat('msgpack6k'),
    new RpcMessagePackCompactSerializationFormat('msgpack6ck'),
);
RpcSerializationFormatResolver.Default = new RpcSerializationFormatResolver(SERIALIZATION_FORMAT);
```

The rebuild matters: `RpcSerializationFormatResolver.Default` snapshots its formats Map at class-load (during `actuallab-rpc` init), so a later `.push()` alone wouldn't reach the existing instance — every consumer would still see "No format with key 'msgpack6ck'". Workers must therefore import `'api'` somewhere in the entry chain before constructing any peer.

The `k`/`ck` formats are wire-equivalent to `msgpack6`/`msgpack6c` on the TS side (TS always emits name-keyed maps via `@msgpack/msgpack`) — the suffix only tells the .NET peer to decode with `KeylessMessagePack` instead of the Key-honoring path.

## Default serializer wiring

- `ByteSerializer.Default = Serializers.MessagePack;` — set by `Serializers.Build` so unit tests that load Core but not Api.Contracts still see the right default.
- `NerdbankMessagePackByteSerializer.Default*` — also synced by `Serializers.Build` for any Fusion code that hasn't migrated to `Serializers.*` yet.
- `RedisSerializer.Default = new(NerdbankMessagePackByteSerializer.Default)` (`src/dotnet/Redis/RedisSerializer.cs`).
- `FlowData.FlowSerializer = new VersionedByteSerializer([NerdbankMessagePackByteSerializer.DefaultTypeDecorating], legacy: MemoryPackByteSerializer.DefaultTypeDecorating)` — the type-decorating Nerdbank wrapper handles polymorphic flow state, MemoryPack stays as the legacy-blob fallback.
- **`KvasSerializer`** — keeps **MemoryPack** as the active serializer (`PreferMemoryPack = true` on the instance you build). Switching KVAS to Nerdbank would orphan every existing KVAS blob; the `MessagePackMarker` byte path is wired but only used by tests that create a non-default instance with `PreferMemoryPack = false`.

## Cross-serializer wire compat

Within Nerdbank, `[Key(N)]` produces array wire identical to MP-CSharp's `[MessagePackObject] [Key(int)]`. Within MemoryPack, `[MemoryPackOrder(N)]` produces its own positional binary wire. They're two different serializers with two different wires — what they share is *the convention that index N means "the same property"*.

For types that need to read both wire formats (e.g. legacy KVAS blobs vs new ones), keep the `MemoryPackOrder` and `Key` indices in sync so the reading side picks the right slot regardless of which wire ends up in the bytes.

Nerdbank does not have a built-in dual-mode reader (try array, fall back to map). If a converter has to span both, write a `MessagePackConverter<T>` that peeks `reader.NextMessagePackType` and dispatches.

## Hand-written converters

Some types ride a hand-written `MessagePackConverter<T>` instead of the auto-generated shape. Two reasons:

- **Identifier ↔ string** — `StringLikeNerdbankConverter<T>` adapts the `IStringLike<T>` types (every `*Id` struct, `Phone`, `Email`, `Emoji`, …) to a plain msgpack string. Registered in bulk via `Serializers.RegisterStringLikeTypes`.
- **Serialize-once fan-out** — `CachingAudioFrameFormatter` / `CachingVideoFrameFormatter` (under `Api/Audio` and `Api/Video`) bypass the auto-generated converter so a single AudioFrame / VideoFrame serializes to bytes once and every fan-out consumer reuses the cached `byte[]`. They write a 4-entry / 11-entry PascalCase map respectively, regardless of whether the serializer is in keyless or Key-honoring mode — the same wire goes to all peers. Registered as one-offs from `ApiModuleInitializer.ModuleInitializer`.

## Aliases / global usings

In the root `Directory.Build.props`:

```xml
<Using Include="Nerdbank.MessagePack" />        <!-- MessagePackSerializer, MessagePackReader, MessagePackWriter, … -->
<Using Include="PolyType" />                    <!-- PropertyShape, ConstructorShape, GenerateShape*, DerivedTypeShape -->
<Using Include="ActualChat.Serialization" />    <!-- Serializers facade -->
<Using Include="Nerdbank.MessagePack.KeyAttribute" Alias="NbKey" />
```

The server-side `Directory.Build.props` (`src/dotnet/.../Server` paths) additionally aliases `System.ComponentModel.DataAnnotations.KeyAttribute` as `KeyAttribute`, so EF entities can use `[Key]` without conflict — and our generated DTOs use `[NbKey(N)]` to be unambiguous.

Convention: **do not** prefix with `Nerdbank.MessagePack.` or `PolyType.` in source. The global usings cover them; for `Key` use `NbKey` if there's any chance of EF being in scope.

## Helpful project pointers

| File | What's in it |
|---|---|
| `src/dotnet/Core/Serialization/Serializers.cs` | Central facade — `MessagePack`, `KeylessMessagePack`, `ClientSide.*`, `RegisterShapeProvider`, `RegisterStringLikeTypes`, `RegisterConverters`, lazy `Build`. |
| `src/dotnet/Core/Module/CoreModuleInitializer.cs` | Hand-written half — declares `MessagePackV6` / `V6C` / `V6K` / `V6CK`, `Configure()` wires them into Fusion's RPC negotiation. |
| `src/dotnet/Core/Module/CoreModuleInitializer.g.cs` | Generated half — `CoreWitness` + `RegisterGenerated`. Same `*ModuleInitializer.g.cs` pattern repeats for every project with `<UsePolyTypeGenerator>true</UsePolyTypeGenerator>`. |
| `src/dotnet/Core/Serialization/SerializerAssociatedTypes.cs` | `[TypeShapeExtension(target, AssociatedTypes = […])]` declarations pairing each open-generic converter with its data type. |
| `src/dotnet/Core/Serialization/FrameworkWitness.cs` | Hand-written witness for framework helper types Fusion doesn't ship a witness for. |
| `src/dotnet/Core/Aot/AotTypes.cs` | `KeepSerializable<T, TWitness>()` helper + `AotTypes.All` aggregation. |
| `src/dotnet/Core/Identifiers/Internal/StringLikeNerdbankConverter.cs` | Identifier ↔ string converter. |
| `src/dotnet/Core/Hosting/Internal/HostRoleNerdbankConverter.cs` | One-off converter pattern reference. |
| `src/dotnet/Core/Serialization/Internal/{Range,SetDiff,Wrapper,Maybe,Box,Nullable,ImmutableDictionary}NerdbankConverter*.cs` | Open-generic converters. |
| `src/dotnet/Api/Module/ApiModuleInitializer.cs` | `StringLikeIdentifiers` registry — single source of truth for both MemoryPack formatter registration and Nerdbank converter registration. Also registers the `Caching*FrameFormatter` instances. |
| `src/dotnet/Api/{Audio,Video}/Caching*FrameFormatter.cs` | Hand-written fan-out-cache `MessagePackConverter<T>`s for AudioFrame / VideoFrame. |
| `src/nodejs/src/api/api.ts` | TS-side bootstrap — registers `msgpack6k`/`msgpack6ck`, sets `RpcSerializationFormatResolver.Default`. |
| `tests/Core.Server.IntegrationTests/AotFormatterPresenceTest.cs` | Trimming sanity check — every `AotTypeKind.Serializable` entry must resolve a converter via both `Serializers.ClientSide.*`. |

## Diagnosing failures

| Symptom | Likely cause |
|---|---|
| `NBMsgPack001: not consistent` on a derived type | Inherited member is in the shape but un-keyed — add `[PropertyShape(Ignore = true)]` to the base member, or add `[Key(N)]` to the derived override. |
| `NBMsgPack003: index collides with` | Derived type re-used a Key from the base. Continue derived from `base_max + 1`. |
| `"The type X has a delayed value that has not been completed"` | Either PolyType picked a self-referencing constructor (add `[ConstructorShape]`) or an open-generic converter is missing a `[TypeShapeExtension]` entry in `SerializerAssociatedTypes.cs`. |
| `MessagePackSerializationException: Unexpected msgpack code 164 (fixstr) encountered` while reading an int | Wire-format mismatch: a Key-honoring deserializer is reading a name-keyed map (or vice-versa). Usually a TS peer talking to a server registered without `msgpack6ck`, or a peer that didn't import `'api'` before constructing its connection. |
| `No format with key 'msgpack6ck'` (TS side) | The worker constructed an RPC peer before the `'api'` module's top-level side-effect ran. Move the `import { Api } from 'api'` to the top of the worker entry. |
| Frame round-trips with default `Offset` / `Duration` / `IsKeyFrame` | Override is missing the explicit `[PropertyShape]` on the derived member — PolyType inherited `Ignore = true` from the abstract base. See *Inheritance → MediaFrame override quirk*. |
| `MessagePackDynamicObjectResolverException: can't find public constructor. type: X` | Code path still on legacy MP-CSharp serializer. Identifier structs (`ChatId`, `StreamId`, …) only deserialize via `StringLikeNerdbankConverter<T>` — make sure their type was registered via `Serializers.RegisterStringLikeTypes` in the owning module's initializer. |
