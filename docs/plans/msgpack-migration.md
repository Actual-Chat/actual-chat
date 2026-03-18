# MemoryPack → MessagePack Migration: Build & Kvas Analysis

This document covers the build infrastructure changes and Kvas serialization
concerns for switching clients from MemoryPack to MessagePack while keeping
MemoryPack enabled on the server.

See also: `docs/plans/msgpack.md` for the type-level migration plan (Phase 1–3).

## Build Infrastructure

### Current defaults (`Directory.Build.props:57-61`)

```xml
<UseMemoryPack Condition="'$(UseMemoryPack)' == ''">true</UseMemoryPack>
<UseMessagePack Condition="'$(UseMessagePack)' == ''">false</UseMessagePack>
<DefineConstants Condition="$(UseMemoryPack)">$(DefineConstants);USE_MEMORYPACK</DefineConstants>
<DefineConstants Condition="$(UseMessagePack)">$(DefineConstants);USE_MESSAGEPACK</DefineConstants>
```

### Plan: flip defaults, override for server

1. **Flip defaults** in `Directory.Build.props`:
   - `UseMemoryPack` → `false`
   - `UseMessagePack` → `true`

2. **Server Docker build** — pass `-p:UseMemoryPack=true` explicitly.
   The server is built in `Dockerfile`, completely independent from client builds:
   - Line 85: `dotnet msbuild /t:GenerateAssemblyNBGVVersionInfo -p:UseMemoryPack=true ActualChat.CI.slnf`
   - Line 89: `dotnet publish ... -p:UseMemoryPack=true ... App.Server.csproj`
   - Lines 94-100: migration builds — add `-p:UseMemoryPack=true` as well

3. **MAUI client builds** — no changes needed.
   Built via `build/Program.cs` targets (`publish-win`, `publish-android`, `publish-ios`)
   on separate CI runners (Windows, macOS). They publish `App.Maui` directly
   and will inherit the new defaults (`UseMemoryPack=false`, `UseMessagePack=true`).

4. **Tests** — `tests/Directory.Build.props` includes `MemoryPack.Generator`
   unconditionally, so tests always have full MemoryPack support regardless of
   the flag.

### Build matrix summary

| Build target | Where | UseMemoryPack | UseMessagePack |
|---|---|---|---|
| Server (Docker) | `Dockerfile` | `true` (explicit `-p:`) | inherits default |
| Migrations (Docker) | `Dockerfile` | `true` (explicit `-p:`) | inherits default |
| MAUI iOS | `build/Program.cs` → macOS runner | `false` (new default) | `true` (new default) |
| MAUI Android | `build/Program.cs` → Windows runner | `false` (new default) | `true` (new default) |
| MAUI Windows | `build/Program.cs` → Windows runner | `false` (new default) | `true` (new default) |
| WASM (Node.js build) | `Dockerfile` Node stage | N/A (JS build) | N/A |
| Tests | CI runners | `true` (unconditional generator) | varies |
| NuGet pack | CI with `-p:PUBLIC_BUILD=true` | new default | new default |

### What the flags control

- **`USE_MEMORYPACK` symbol** → enables real MemoryPack source generators and
  NuGet packages. When `false`, shim attributes in
  `src/dotnet/Core/Serialization/Shims/MemoryPack.cs` replace them (code compiles,
  but no MemoryPack serialization at runtime).

- **`USE_MESSAGEPACK` symbol** → switches RPC default format in
  `CoreSerializerAndRpcSetup.cs` from `MemoryPackV6`/`MemoryPackV6C` to
  `MessagePackV6`/`MessagePackV6C`.

- **RPC format negotiation**: Server registers all formats in
  `RpcSerializationFormat.All` (MemoryPack + MessagePack + JSON). Client requests
  its preferred format; server can serve any. So a MessagePack client talks to a
  MemoryPack-capable server with no issues.

## Kvas Serialization

### Planned replacement

The current Kvas approach (client serializes to opaque `byte[]`, server stores
blindly) will be replaced by a **new account settings service** that serves
properly typed objects instead of binary blobs. This eliminates the client-side
binary serialization problem entirely — the RPC layer handles serialization
transparently using whatever format the client negotiates (MessagePack, JSON,
etc.).

This is blocked on a Fusion update that adds `[RpcSerializable]` support.
Once available, account-related settings will migrate from Kvas to this new
service, and the Kvas binary format concerns below become moot for those types.

Until then, the current Kvas architecture remains in place:

### Architecture

```
Client (serializes/deserializes)
  → KvasExt.Get<T>() / Set<T>()
    → KvasSerializer.Read() / Write()
      → raw byte[] with format marker prefix
        → sent over RPC as opaque bytes
          → Server stores in PostgreSQL as-is (never deserializes)
```

The server is a **dumb byte-bag** for Kvas — it stores and retrieves `byte[]`
without interpreting the content. All serialization happens on the client.

Exception: `InvitesBackend` and `AudioStreamingBackend` construct their own
`AccountSettings` server-side to read `UserChatSettings`, so the server does
deserialize Kvas values in those two places.

### KvasSerializer format (`src/dotnet/Core/Kvas/KvasSerializer.cs`)

- **Write**: prepends a 1-byte marker, then binary payload
  - `0x0` → MemoryPack (current default)
  - `0x1` → MessagePack (when `USE_MESSAGEPACK_IN_KVAS` is defined)
- **Read**: checks first byte to pick deserializer, with JSON fallback for
  unmarked legacy data

### What's stored in Kvas

#### Server-side (AccountSettings → PostgreSQL via `IServerKvas` RPC)

| Accessor | Type | Content |
|---|---|---|
| `UserLanguageSettings()` | `UserLanguageSettings` | Preferred language |
| `UserAppSettings()` | `UserAppSettings` | App-level preferences |
| `UserEmailsSettings()` | `UserEmailsSettings` | Email notification prefs |
| `UserListeningSettings()` | `UserListeningSettings` | Default listening mode |
| `UserNavbarSettings()` | `UserNavbarSettings` | Navbar configuration |
| `UserTranscriptionEngineSettings()` | `UserTranscriptionEngineSettings` | Transcription engine prefs |
| `UserReactionSettings()` | `UserReactionSettings` | Reaction preferences |
| `UserAvatarSettings()` | `UserAvatarSettings` | Avatar selection |
| `UserChatRecordingDetectedLanguage()` | `UserChatRecordingDetectedLanguage` | Last detected recording language |
| `UserChatSettings(chatId)` | `UserChatSettings` | Per-chat: voice mode, listening mode, notifications, translation settings |

All defined via `IHasKvasKey<T>` in `src/dotnet/Api/Users/`. Extension methods
in `src/dotnet/Api/Users/KvasExt.cs`.

#### Client-side (LocalSettings → IndexedDB/SQLite, never hits server)

| Key pattern | Type | Content |
|---|---|---|
| `LocalAppSettings` | `LocalAppSettings` | Log viewer, camera selection, background blur |
| `LocalOnboardingSettings` | `LocalOnboardingSettings` | Onboarding steps, cookie consent |
| `SelectedChatId` | `ChatId` | Last selected chat |
| `SelectedChatIds` | (collection) | Selected chats |
| `ActiveChats` | (collection) | Active chat list |
| `SelectedNavbarGroupId` | `Symbol` | Navbar group selection |
| `ChatListSettings({placeId})` | `ChatListSettings` | Per-place chat list sort/filter |
| `MessageDraft.{chatId}.RelatedEntry` | `RelatedEntryRef?` | Reply-to draft |
| `RightPanel.*` | (state) | Right panel open/close |
| `FakeDeviceContacts/Options` | `FakeDeviceContactOptions` | Dev/test only |

LocalSettings backends:
- **WASM**: `WebKvasBackend` → IndexedDB
- **MAUI**: `SQLiteBatchingKvasBackend` → `LocalSettings.db3`

### Migration concerns

#### 1. Old MemoryPack-encoded data in PostgreSQL

All existing server-side Kvas entries are MemoryPack-encoded (`0x0` marker).
After clients switch to MessagePack, new writes will be MessagePack-encoded
(`0x1` marker). The read path already handles both formats transparently via
the marker byte.

**However**, if `UseMemoryPack=false` on clients, the MemoryPack NuGet is
replaced by shim attributes — `MemoryPackByteSerializer` won't actually work
at runtime. Reading old `0x0`-marked data will fail.

Options:
- **A) Server-side migration**: One-time script to re-encode all `kvas_entries`
  from MemoryPack to MessagePack. Then clients never need MemoryPack.
- **B) Keep MemoryPack reading on clients**: Keep the MemoryPack NuGet for
  deserialization only, even when `UseMemoryPack=false`. Requires splitting
  the flag into "generate" vs "read-only".
- **C) JSON fallback**: On read failure, return `null` (already happens —
  `KvasSerializer.Read` catches `MemoryPackSerializationException` and returns
  `null`). Client gets defaults, then re-saves in MessagePack on next write.
  Acceptable for settings but lossy.

#### 2. Local client storage (IndexedDB/SQLite)

Ephemeral, device-local data. Same marker-based read logic applies. After an
app update, old MemoryPack entries may fail to deserialize, but:
- Read failure → `null` → client gets default values
- Next write saves in new format
- No meaningful data loss (selected chat, draft replies, etc.)

This is a non-issue — clearing local storage on app update is acceptable if
needed.

#### 3. Server-side Kvas reads

`InvitesBackend` and `AudioStreamingBackend` construct `AccountSettings`
server-side to read `UserChatSettings`. Since the server keeps
`UseMemoryPack=true`, it can read both old (MemoryPack) and new (MessagePack)
entries — no issue here.

## Files Reference

| Item | File |
|---|---|
| UseMemoryPack/UseMessagePack defaults | `Directory.Build.props:57-61` |
| MemoryPack NuGet versions | `Directory.Packages.props:21,158-160` |
| MemoryPack.Generator (src) | `src/dotnet/Directory.Build.props:131` |
| MemoryPack.Generator (tests, unconditional) | `tests/Directory.Build.props:51` |
| MemoryPack shim attributes | `src/dotnet/Core/Serialization/Shims/MemoryPack.cs` |
| Global using MemoryPack | `src/dotnet/Directory.Build.props:92` |
| RPC format selection | `src/dotnet/Core/Module/CoreSerializerAndRpcSetup.cs` |
| KvasSerializer | `src/dotnet/Core/Kvas/KvasSerializer.cs` |
| AccountSettings class | `src/dotnet/Api.Contracts/Kvas/AccountSettings.cs` |
| User settings accessors | `src/dotnet/Api/Users/KvasExt.cs` |
| LocalSettings class | `src/dotnet/Core/Kvas/LocalSettings.cs` |
| MemoryPack formatter registry | `src/dotnet/Api/Module/ApiModuleInitializer.cs` |
| Server Dockerfile | `Dockerfile` |
| MAUI build targets | `build/Program.cs:312-432` |
| GitHub Actions main workflow | `.github/workflows/build-test-deploy-dev.yml` |
