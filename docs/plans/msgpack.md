# MessagePack Migration Plan

> **Status:** Phases 1–3 are complete; phase 4 (removing `[DataContract]`) is next.
> For the resulting target state — which serializer owns which path, and the attribute
> convention — see [Serialization](../architecture/serialization.md).

## Goal

Replace MemoryPack with MessagePack as the primary binary serializer across the entire codebase. MessagePack provides better cross-platform support (especially for non-.NET clients) and a more mature ecosystem.

## Current State

### Serialization attributes in `src/dotnet/`

| Attribute | File count | Description |
|-----------|-----------|-------------|
These were the counts when this plan was written; see the status note above for where things
stand now.

| Attribute | File count | Description |
|-----------|-----------|-------------|
| `[DataContract]` | 252 | Types with serialization metadata |
| `[MemoryPackable]` | 250 | Types with MemoryPack support (the binary serializer at the time) |
| `[MessagePackFormatter]` | 35 | Identifier types with custom MessagePack formatters (already migrated) |
| `[MessagePackObject]` | 2 | Types with native MessagePack object support (`Maybe<T>`, `Choice<T, TAlt>`) |

`[DataContract]` was described here as "used by MessagePack for key mapping". That is only
true for types *without* `[MessagePackObject]`, and only on the dynamic (non-AOT) resolver —
where `[MessagePackObject]` + `[Key]` is present, `[Key]` wins and the DataContract
annotations have no effect on the wire format. See
[Serialization](../architecture/serialization.md) for the verified matrix.

### What already has MessagePack support

1. **All 33 identifier types** (`src/dotnet/Api/Identifiers/`) - use `StringIdentifierMessagePackFormatter<T>` custom formatters. These serialize as strings, same as JSON.
2. **`Maybe<T>`** and **`Choice<T, TAlt>`** (`src/dotnet/Core/`) - use `[MessagePackObject(true, SuppressSourceGeneration = true)]` with `[Key]` attributes.

### What still needs MessagePack support

~250 types with `[MemoryPackable]` but no `[MessagePackObject]`. These span all domains:

- **Chat**: Chat, ChatEntry, ChatDiff, Author, AuthorFull, Place, Role, Reaction, Conversation, Translation, etc.
- **Users**: Account, AccountFull, Avatar, AvatarFull, all UserXxxSettings types, ChatPosition, etc.
- **Contacts**: Contact, ExternalContact, ExternalContactFull, ThreadContact, etc.
- **Media**: Media, LinkPreview, GrabStatus, Upload, MediaRef, Picture, etc.
- **Notifications**: Notification, Device, ExplicitNotification, ChatNotificationOption, etc.
- **Streaming**: AudioRecord, LiveStreamInfo, etc.
- **Core**: Change\<T>, Expiring\<T>, LinearMap, SetDiff, SearchMatch, SecureToken, HashString, etc.
- **Backend events**: TextEntryChangedEvent, ChatChangedEvent, AccountChangedEvent, ~15 event types
- **API commands**: ~40 command types in Api.Contracts
- **Backend commands**: ~50 command types in \*.Contracts projects

## Phase 1: Serialization Tests (COMPLETE)

Comprehensive serialization tests have been written for all serializable types. These tests use `PassThroughAllSerializers()` / `AssertPassesThroughAllSerializers()` which round-trips values through **all** serializers: System.Text.Json, Newtonsoft.Json, MessagePack, MemoryPack, NerdbankMessagePack, and type-decorating variants.

### Test files and counts

| Project | Test File | Tests |
|---------|-----------|-------|
| Core.UnitTests | `CoreSerializationTest.cs` | 13 |
| Core.UnitTests | `Identifiers/IdentifierSerializationTest.cs` | 26 |
| Chat.UnitTests | `ChatModelSerializationTest.cs` | 34 |
| Chat.UnitTests | `ChatCommandSerializationTest.cs` | 31 |
| Chat.UnitTests | `ChatBackendCommandSerializationTest.cs` | 33 |
| Chat.UnitTests | `ChatEventSerializationTest.cs` | 13 |
| Chat.UnitTests | `SearchTranscriptionSerializationTest.cs` | 14 |
| Chat.UnitTests | `InviteDetailsSerializationTest.cs` | 10 |
| Chat.UnitTests | `AuthorSerialization.cs` | 1 |
| Users.UnitTests | `UserModelSerializationTest.cs` | 17 |
| Users.UnitTests | `UserCommandSerializationTest.cs` | 22 |
| Contacts.UnitTests | `ContactSerializationTest.cs` | 19 |
| Media.UnitTests | `MediaSerializationTest.cs` | 9 |
| Streaming.UnitTests | `StreamingSerializationTest.cs` | 2 |
| Notifications.IntegrationTests | `NotificationSerializationTests.cs` | 22 |
| **Total** | | **~340** |

**All 340 tests pass** across all 7 test projects (0 failures, 3 skips).

### Test patterns used

- **Structural equality types**: `value.AssertPassesThroughAllSerializers()` - simple round-trip + equality check
- **Reference equality types**: `value.AssertPassesThroughAllSerializers(action, Out)` + field-by-field assertions (for types with arrays, collections, or `ReferenceEquals`-based equality)

### Source fixes made during Phase 1

- `src/dotnet/Chat.Contracts/TextEntry.cs` - Added `[Newtonsoft.Json.JsonConstructor]` attribute to resolve constructor ambiguity for Newtonsoft.Json deserialization.

### Known issues — resolved

1. ~~**`ChatEntryLanguage`** has mixed MessagePack key types~~ — no longer an issue. `[MessagePackObject(true)]` (keyAsPropertyName) ignores `MemoryPackOrder` and uses property names as keys, so there's no key-type mix. Tests now exercise the full serializer suite.
2. ~~**`UserAvatarSettings.AvatarIds`** and **`UserBubbleSettings.ReadBubbles`** serialized through private `LegacyXxx` properties~~ — refactored: both types now expose `ApiArray<Symbol>` as public `[DataMember, MemoryPackOrder(0)]` properties. Wire-compatible with old MemoryPack data (same type at same order); MessagePack now roundtrips correctly via `ApiArrayMessagePackFormatter<Symbol>`.

### Design patterns to preserve (not bugs)

- **`ApiNullable8<T>` MemoryPack bridge** (e.g., `ChatEntry.MemoryPackEndsAt`, `Notification.MemoryPackHandledAt`, `Upload.MemoryPackLength`, `Device.MemoryPackAccessedAt`, `LegacyChatEntry.MemoryPack*`, `ChatEntryAudio.MemoryPack*`, `ChatRangeMeta.MemoryPack*`, `ChatEntryRangeMeta.MemoryPack*`): a private `ApiNullable8<T>` property with `[MemoryPackInclude, MemoryPackOrder(N), IgnoreMember]` shadows a public `Nullable<T>` property with `[MemoryPackIgnore]`. MemoryPack serializes the private 8-byte bridge; MessagePack serializes the public `Nullable<T>` natively. Both serializers roundtrip correctly. When/if MemoryPack is removed in Phase 3, drop the private bridges and keep the public `Nullable<T>` properties.

## Phase 2: Add `[MessagePackObject]` Attributes (COMPLETE)

For each type, add `[MessagePackObject]` with integer `[Key(N)]` attributes on all serialized members. This is the bulk of the migration work.

### Migration pattern per type

```csharp
// Before:
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record SomeType(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version
);

// After:
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record SomeType(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] string Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] long Version
);
```

### Suggested order

1. **Core types first** (Change\<T>, Expiring\<T>, LinearMap, SetDiff, HashString, etc.) - these are dependencies for other types
2. **Model types** (Chat, ChatEntry, Author, Place, Role, Account, Avatar, Contact, Media, etc.) - the main domain objects
3. **Diff types** (ChatDiff, PlaceDiff, RoleDiff, ConversationDiff, TranslationDiff, etc.)
4. **Command types** (API + backend commands) - these are the RPC payloads
5. **Event types** (backend events) - these are the event bus payloads
6. **Settings types** (UserXxxSettings, LocalAppSettings) - may need special handling for legacy properties

### Verification

After adding `[MessagePackObject]` to a type, its serialization test should continue to pass (including the MessagePack round-trip). Run:

```bash
dotnet test tests/<Project>.csproj --filter "TypeName_Basic"
```

## Phase 3: Remove MemoryPack (COMPLETE)

Once all types have MessagePack support and tests pass:

1. Remove `[MemoryPackable]` attributes and `[MemoryPackOrder]` annotations
2. Remove MemoryPack NuGet packages
3. Update serializer configuration to use MessagePack as the default binary serializer
4. Remove any MemoryPack-specific bridges (e.g., `ApiNullable8`)

**Part 1 (in review)** removes MemoryPack from everything except the two paths that still
read pre-existing database bytes: Flow state (`FlowData.FlowSerializer`'s legacy leg) and
legacy server KVAS values (`KvasSerializer`'s `0x0` marker). It also drops the
`MemoryPackV5`/`V6` RPC formats, trims the `StringLikeMemoryPackFormatter` registrations, and
deletes the `ApiNullable8` bridges (step 4). Retained: all Flow types and their reachable
state, the `StoredSettings` union, the legacy `Language` formatters, `MediaRef`, `Range<T>`,
`ChatEntrySlim`, and `HashedExternalContact`.

MemoryPack cannot be removed outright while those DB blobs exist — steps 2 and 3 stay open
until the persisted data is migrated or aged out.

What remains carries MemoryPack: 68 types across 83 files, all inside the retained closure
(Flows and their reachable state, the `StoredSettings` union, the legacy `Language` formatters,
`MediaRef`, `Range<T>`, `ChatEntrySlim`, `HashedExternalContact`). `AotFormatterPresenceTest`
resolves all 32 of the MemoryPack-serializable AOT entries plus 315 MessagePack ones without
dynamic IL emit, and the AOT keep-lists have been regenerated against that set.

## Phase 4: Remove `[DataContract]` / `[DataMember]` (PLANNED)

Independent of MemoryPack. `[DataContract]` is read by Newtonsoft.Json (where it switches the
type to opt-in) and, for types without `[MessagePackObject]`, by MessagePack's dynamic
resolver — while System.Text.Json ignores it entirely. Replacing it with serializer-native
attributes removes that ambiguity. The rationale, the verified behavior matrix, and the
per-type migration risk are documented in
[Serialization](../architecture/serialization.md#migrating-off-datacontract).
