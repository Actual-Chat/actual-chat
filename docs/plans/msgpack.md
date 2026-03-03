# MessagePack Migration Plan

## Goal

Replace MemoryPack with MessagePack as the primary binary serializer across the entire codebase. MessagePack provides better cross-platform support (especially for non-.NET clients) and a more mature ecosystem.

## Current State

### Serialization attributes in `src/dotnet/`

| Attribute | File count | Description |
|-----------|-----------|-------------|
| `[DataContract]` | 252 | Types with serialization metadata (used by MessagePack for key mapping) |
| `[MemoryPackable]` | 250 | Types with MemoryPack support (the current binary serializer) |
| `[MessagePackFormatter]` | 35 | Identifier types with custom MessagePack formatters (already migrated) |
| `[MessagePackObject]` | 2 | Types with native MessagePack object support (`Maybe<T>`, `Choice<T, TAlt>`) |

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
| Notification.IntegrationTests | `NotificationSerializationTests.cs` | 22 |
| **Total** | | **~340** |

**All 340 tests pass** across all 7 test projects (0 failures, 3 skips).

### Test patterns used

- **Structural equality types**: `value.AssertPassesThroughAllSerializers()` - simple round-trip + equality check
- **Reference equality types**: `value.PassThroughAllSerializers(Out)` + field-by-field assertions (for types with arrays, collections, or `ReferenceEquals`-based equality)
- **Types with broken MessagePack**: Individual serializer tests excluding MessagePack (e.g., `ChatEntryLanguage` has mixed MessagePack key types)
- **Types with private legacy properties**: Individual JSON + MemoryPack serializer tests (e.g., `UserAvatarSettings.LegacyAvatarIds`)

### Source fixes made during Phase 1

- `src/dotnet/Chat.Contracts/TextEntry.cs` - Added `[Newtonsoft.Json.JsonConstructor]` attribute to resolve constructor ambiguity for Newtonsoft.Json deserialization.

### Known issues discovered

1. **`ChatEntryLanguage`** has mixed MessagePack key types (string + int keys on the same type), causing `MessagePackDynamicObjectResolverException`. This cascades to `ChatLanguageTile` which contains it.
2. **`UserAvatarSettings.AvatarIds`** and **`UserBubbleSettings.ReadBubbles`** are serialized through private `LegacyXxx` properties that MessagePack can't see. These need to be refactored or have custom formatters.
3. Some types use `ApiNullable8` MemoryPack bridge for `Moment?` fields (e.g., `ChatEntry`, `Notification`) - these need attention during migration.

## Phase 2: Add `[MessagePackObject]` Attributes (TODO)

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

## Phase 3: Remove MemoryPack (TODO)

Once all types have MessagePack support and tests pass:

1. Remove `[MemoryPackable]` attributes and `[MemoryPackOrder]` annotations
2. Remove MemoryPack NuGet packages
3. Update serializer configuration to use MessagePack as the default binary serializer
4. Remove any MemoryPack-specific bridges (e.g., `ApiNullable8`)
