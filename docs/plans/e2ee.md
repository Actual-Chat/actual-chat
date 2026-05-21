# E2E Encryption Implementation Plan

## Overview
Add end-to-end encryption to ActualChat. When enabled on a chat (by any owner via the right panel toggle), only text messages are allowed and all message content is encrypted client-side before sending to the server.

### Crypto Design
- **AES-256-GCM** symmetric key per chat (group key)
- **ECDH P-256** identity key pairs for each user (key agreement to distribute group keys)
- **HKDF-SHA256** for key derivation from ECDH shared secret
- Key rotation on member changes (new members can't read old messages)
- All crypto via `System.Security.Cryptography` (no new NuGet packages)
- Private keys stored in `LocalSettings` (existing Kvas abstraction)

### Encrypted Message Format
```
e2e:1:{keyVersion}:{base64_iv}:{base64_ciphertext}
```
Detection: `Content.StartsWith("e2e:")`

---

## Phase 1: Data Model Changes

### 1.1 Add `IsE2EEncrypted` to Chat model
**File:** `src/dotnet/Api/Chat/Chat.cs`
- Add property `[DataMember, MemoryPackOrder(19)] public bool IsE2EEncrypted { get; init; }` to `Chat` record
- Add property `[DataMember, MemoryPackOrder(16)] public bool? IsE2EEncrypted { get; init; }` to `ChatDiff` record

### 1.2 Update DB entity
**File:** `src/dotnet/Chat.Service/Db/DbChat.cs`
- Add `public bool IsE2EEncrypted { get; set; }`
- Update `ToModel()` and `UpdateFrom()` to include the field

### 1.3 Create E2E key DB entities
**New file:** `src/dotnet/Chat.Service/Db/DbE2EUserPublicKey.cs`
```
Table: e2e_user_public_keys
- Id (string, PK) = UserId
- PublicKeyBase64 (string) - ECDH P-256 public key
- Version (long) - for concurrency
- CreatedAt (DateTime)
```

**New file:** `src/dotnet/Chat.Service/Db/DbE2EChatGroupKey.cs`
```
Table: e2e_chat_group_keys
- Id (string, PK) = "{chatId}:{keyVersion}"
- ChatId (string, indexed)
- KeyVersion (int)
- WrappedKeysJson (string) - JSON: {"userId1": "base64WrappedKey1", ...}
- CreatedAt (DateTime)
```

### 1.4 Register in DbContext
**File:** `src/dotnet/Chat.Service/Db/ChatDbContext.cs`
- Add `DbSet<DbE2EUserPublicKey>` and `DbSet<DbE2EChatGroupKey>`
- Add model configuration in `OnModelCreating()` following existing patterns (UseCollation, indexes)

### 1.5 Create DB migration
Run `dotnet ef migrations add AddE2EEncryption` for the Chat.Service project.

---

## Phase 2: E2E Crypto Service (Core)

### 2.1 Create crypto operations service
**New file:** `src/dotnet/Core/Security/E2ECrypto.cs`

Static methods (pure crypto, no dependencies):
- `GenerateIdentityKeyPair()` → `(byte[] publicKey, byte[] privateKey)` using `ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)`
- `GenerateGroupKey()` → `byte[32]` using `RandomNumberGenerator`
- `WrapGroupKey(byte[] groupKey, byte[] recipientPublicKey, byte[] senderPrivateKey)` → `byte[]` — ECDH → HKDF → AES-256-GCM wrap
- `UnwrapGroupKey(byte[] wrapped, byte[] senderPublicKey, byte[] recipientPrivateKey)` → `byte[]` — reverse of above
- `EncryptContent(string plaintext, byte[] groupKey, int keyVersion)` → `string` — returns `e2e:1:{ver}:{iv}:{ct}`
- `DecryptContent(string encrypted, byte[] groupKey)` → `string` — parses format, decrypts
- `IsEncrypted(string content)` → `bool` — checks `e2e:` prefix

---

## Phase 3: API Contracts & Backend Service

### 3.1 API models
**New file:** `src/dotnet/Api/Chat/E2EUserPublicKey.cs`
```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record E2EUserPublicKey(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] string PublicKeyBase64,
    [property: DataMember, MemoryPackOrder(2)] long Version
);
```

**New file:** `src/dotnet/Api/Chat/E2EChatGroupKey.cs`
```csharp
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record E2EChatGroupKey(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] int KeyVersion,
    [property: DataMember, MemoryPackOrder(2)] ImmutableDictionary<string, string> WrappedKeys // userId → base64
);
```

### 3.2 Frontend API contract
**New file:** `src/dotnet/Api.Contracts/Chat/IE2EKeys.cs`
```csharp
public interface IE2EKeys : IComputeService
{
    [ComputeMethod]
    Task<E2EUserPublicKey?> GetUserPublicKey(Session session, UserId userId, CancellationToken ct);

    [ComputeMethod]
    Task<E2EChatGroupKey?> GetLatestChatGroupKey(Session session, ChatId chatId, CancellationToken ct);

    [ComputeMethod]
    Task<E2EChatGroupKey?> GetChatGroupKey(Session session, ChatId chatId, int keyVersion, CancellationToken ct);

    [CommandHandler]
    Task OnPublishUserPublicKey(E2EKeys_PublishUserPublicKey command, CancellationToken ct);

    [CommandHandler]
    Task OnPublishChatGroupKey(E2EKeys_PublishChatGroupKey command, CancellationToken ct);
}
// Commands: E2EKeys_PublishUserPublicKey(Session, PublicKeyBase64)
// Commands: E2EKeys_PublishChatGroupKey(Session, ChatId, KeyVersion, WrappedKeys)
```

### 3.3 Backend contract
**New file:** `src/dotnet/Chat.Contracts/IE2EKeysBackend.cs`
Following `IChatsBackend` pattern with backend-only commands.

### 3.4 Backend implementation
**New file:** `src/dotnet/Chat.Service/E2EKeysBackend.cs`
- `class E2EKeysBackend : DbServiceBase<ChatDbContext>, IE2EKeysBackend`
- Implements CRUD for public keys and chat group keys
- Uses EF Core with existing DB context

### 3.5 Frontend implementation
**New file:** `src/dotnet/Chat.Service/E2EKeys.cs`
- `class E2EKeys : IE2EKeys` (following `Chats.cs` pattern)
- Session validation, permission checks (must be chat member)
- Delegates to `IE2EKeysBackend`

### 3.6 Register in module
**File:** `src/dotnet/Chat.Service/Module/ChatServiceModule.cs`
Add:
```csharp
rpcHost.AddApi<IE2EKeys, E2EKeys>();
rpcHost.AddBackend<IE2EKeysBackend, E2EKeysBackend>();
```

---

## Phase 4: Client-Side Key Management

### 4.1 Client key store
**New file:** `src/dotnet/UI.Blazor.App/Services/E2E/E2EKeyStore.cs`
- Uses `LocalSettings` (Kvas) to store private key under key `"e2e.identity.privateKey"`
- `GetOrCreateIdentityKeyPair()` — generates if not exists, returns (publicKeyBase64, privateKeyBytes)
- `GetPrivateKey()` → `byte[]`
- Automatically publishes public key to server on first creation

### 4.2 Client chat key cache
**New file:** `src/dotnet/UI.Blazor.App/Services/E2E/E2EChatKeyCache.cs`
- In-memory cache of decrypted group keys: `Dictionary<(ChatId, int keyVersion), byte[]>`
- `GetGroupKey(ChatId, int keyVersion)` — fetches from server if not cached, unwraps using private key
- `GetLatestGroupKey(ChatId)` — fetches latest version
- `InvalidateChat(ChatId)` — clears cache for rotation

### 4.3 Client crypto facade
**New file:** `src/dotnet/UI.Blazor.App/Services/E2E/E2EChatCrypto.cs`
- High-level service orchestrating key store + cache + crypto
- `EncryptMessage(ChatId chatId, string plaintext)` → `string` (encrypted format)
- `DecryptMessage(ChatId chatId, string content)` → `string` (plaintext)
- `InitializeChatE2E(ChatId chatId, AuthorId[] members)` — generates group key, wraps for all members, publishes
- `RotateChatKey(ChatId chatId, AuthorId[] members)` — new group key, distribute

### 4.4 Register services
**File:** `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`
Register `E2EKeyStore`, `E2EChatKeyCache`, `E2EChatCrypto` as scoped/singleton services.

---

## Phase 5: Message Encryption (Send Path)

### 5.1 Encrypt before sending
**File:** `src/dotnet/UI.Blazor.App/Services/SendingMessages/SendingMessages.cs`

In `ProcessCommand()` (line ~436), before creating `Chats_UpsertTextEntry`:
```csharp
string textToSend = request.Text;
if (chat?.IsE2EEncrypted == true) {
    var e2eCrypto = Services.GetRequiredService<E2EChatCrypto>();
    textToSend = await e2eCrypto.EncryptMessage(request.ChatId, request.Text);
}
var cmd = new Chats_UpsertTextEntry(Session, request.ChatId, request.LocalId, textToSend, ...);
```

Need to fetch chat to check `IsE2EEncrypted` — already available via `Hub.Chats.Get()`.

---

## Phase 6: Message Decryption (Display Path)

### 6.1 Decrypt in message view
**File:** `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor`

In `OnParametersSetAsync()` (line ~242), before calling `ChatMarkupHub.GetMarkup()`:
```csharp
var entry = Entry;
if (E2ECrypto.IsEncrypted(entry.Content)) {
    try {
        var decrypted = await E2EChatCrypto.DecryptMessage(entry.ChatId, entry.Content);
        entry = entry with { Content = decrypted };
    } catch {
        entry = entry with { Content = "[Unable to decrypt message]" };
    }
}
_markupTask = ChatMarkupHub.GetMarkup(entry, MarkupConsumer.MessageView, ct).AsTask();
```

### 6.2 Decrypt in other display locations
Also handle decryption in:
- `ChatMessageQuote` (reply preview)
- Chat list last message preview
- Notification text generation
- Search results (show "[Encrypted message]" placeholder)

---

## Phase 7: UI — E2E Toggle in Right Panel

### 7.1 Add toggle to right panel
**File:** `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelChatInfo.razor`

Following the exact pattern of the "Summarize" toggle (lines 23-36):
```razor
@if (m.ShowE2EToggle) {
    var iconCls = m.IsE2EEncrypted ? "text-primary" : "text-03";
    <RightPanelChatInfoItem
        Click="@(_ => OnE2EToggleChanged(!m.IsE2EEncrypted))"
        Content="E2E encryption"
        Hint="@(m.IsE2EEncrypted ? "On" : "Off")">
        <Icon><i class="text-2xl icon-lock @iconCls"></i></Icon>
        <Right><Toggle Class="mr-2" IsChecked="@m.IsE2EEncrypted"/></Right>
    </RightPanelChatInfoItem>
}
```

Update `Model` record to include `ShowE2EToggle` and `IsE2EEncrypted`.
Update `ComputeState()` — show toggle for owners of non-public chats.

### 7.2 Toggle handler
`OnE2EToggleChanged(bool isEnabled)`:
- If enabling:
  1. Ensure user has identity key pair (via `E2EKeyStore`)
  2. Fetch all member public keys
  3. Generate group key, wrap for each member
  4. Publish wrapped keys via `E2EKeys_PublishChatGroupKey`
  5. Update chat via `Chats_Change` with `ChatDiff { IsE2EEncrypted = true }`
- If disabling:
  1. Update chat via `Chats_Change` with `ChatDiff { IsE2EEncrypted = false }`
  2. Old messages remain encrypted (still decryptable by members who have the key)

---

## Phase 8: UI — Restrictions in E2E Chats

### 8.1 Disable non-text features in message editor
**File:** `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditor.razor`

- In `Post()` (line ~190): reject if attachments present and chat is E2E
- Hide/disable attachment button when `Chat.IsE2EEncrypted`
- Hide/disable audio recording button
- Show lock icon or "E2E encrypted" indicator in editor area

### 8.2 Block audio/video recording
**File:** Components that handle audio recording (`ChatAudioPanel`, `RecorderToggle`, etc.)
- Check `Chat.IsE2EEncrypted` and disable recording UI

### 8.3 Block forwarding TO/FROM E2E chats
- In forward destination selection: exclude E2E chats
- In E2E chat: hide forward option on messages

### 8.4 Server-side validation
**File:** `src/dotnet/Chat.Service/Chats.cs`

In `OnUpsertTextEntry()` (line ~370):
- If chat is E2E encrypted, reject messages with attachments or audio links
- Allow only plain text content

In `OnChange()` (chat update handler):
- When `IsE2EEncrypted` changes, validate user is owner

---

## Phase 9: Key Rotation on Member Changes

### 9.1 Handle member add/remove
When a member is added or removed from an E2E chat, the group key must be rotated.

**Approach**: Client-side rotation triggered by the member-change event:
- The member who performed the add/remove action triggers rotation
- Their client generates a new group key, wraps for current members, publishes
- New members only get the new key (can't read old messages)
- Removed members don't get the new key (can't read new messages)

**File:** `src/dotnet/UI.Blazor.App/Components/ChatSettings/EditChatMembersModalPage.razor` (or wherever members are added/removed)
- After successful add/remove, call `E2EChatCrypto.RotateChatKey()`

---

## Phase 10: Visual Indicators

### 10.1 Lock icon in chat header/list
- Show a lock icon next to E2E encrypted chat titles
- Show "Messages are end-to-end encrypted" banner in chat view

### 10.2 Indicator for pre-E2E messages
- Messages sent before E2E was enabled appear as normal (unencrypted)
- Messages that can't be decrypted show "[Encrypted message]" placeholder

---

## Key Files Summary

### New files to create:
| File | Purpose |
|------|---------|
| `src/dotnet/Core/Security/E2ECrypto.cs` | Core crypto operations (static) |
| `src/dotnet/Api/Chat/E2EUserPublicKey.cs` | User public key API model |
| `src/dotnet/Api/Chat/E2EChatGroupKey.cs` | Chat group key API model |
| `src/dotnet/Api.Contracts/Chat/IE2EKeys.cs` | Frontend API contract + commands |
| `src/dotnet/Chat.Contracts/IE2EKeysBackend.cs` | Backend contract + commands |
| `src/dotnet/Chat.Service/E2EKeys.cs` | Frontend implementation |
| `src/dotnet/Chat.Service/E2EKeysBackend.cs` | Backend implementation |
| `src/dotnet/Chat.Service/Db/DbE2EUserPublicKey.cs` | DB entity |
| `src/dotnet/Chat.Service/Db/DbE2EChatGroupKey.cs` | DB entity |
| `src/dotnet/UI.Blazor.App/Services/E2E/E2EKeyStore.cs` | Client private key storage |
| `src/dotnet/UI.Blazor.App/Services/E2E/E2EChatKeyCache.cs` | Client group key cache |
| `src/dotnet/UI.Blazor.App/Services/E2E/E2EChatCrypto.cs` | Client crypto facade |

### Existing files to modify:
| File | Changes |
|------|---------|
| `src/dotnet/Api/Chat/Chat.cs` | Add `IsE2EEncrypted` to `Chat` and `ChatDiff` |
| `src/dotnet/Chat.Service/Db/DbChat.cs` | Add `IsE2EEncrypted` column |
| `src/dotnet/Chat.Service/Db/ChatDbContext.cs` | Add DbSets and model config |
| `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` | Register E2E services |
| `src/dotnet/Chat.Service/Chats.cs` | Validation: E2E chats reject non-text |
| `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelChatInfo.razor` | E2E toggle |
| `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditor.razor` | Disable non-text in E2E chats |
| `src/dotnet/UI.Blazor.App/Services/SendingMessages/SendingMessages.cs` | Encrypt before send |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor` | Decrypt before display |
| `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` | Register client E2E services |

---

## Files to Scan When Resuming

These files should be re-read at implementation time for exact patterns and line numbers:

### Core architecture patterns:
- `src/dotnet/Chat.Contracts/IChatsBackend.cs` — backend command patterns (IBackendCommand, IHasShardKey)
- `src/dotnet/Chat.Service/ChatsBackend.cs` — DbServiceBase implementation pattern
- `src/dotnet/Chat.Service/Module/ChatServiceModule.cs` — service registration pattern (rpcHost.AddApi/AddBackend)
- `src/dotnet/Core/Kvas/LocalSettings.cs` — client-side key-value storage API

### Message flow:
- `src/dotnet/UI.Blazor.App/Services/SendingMessages/SendingMessages.cs` — ProcessCommand() encryption hook
- `src/dotnet/UI.Blazor.App/Services/SendingMessages/SendMessageRequest.cs` — request model
- `src/dotnet/Chat.Service/Chats.cs` — OnUpsertTextEntry() server-side validation

### Display pipeline:
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor` — OnParametersSetAsync(), ChatMarkupHub.GetMarkup() call
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageInternalView/ChatEntryMessageInternalView.razor` — Markup rendering
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Threads/ThreadMessageView.razor` — thread message display

### UI settings patterns:
- `src/dotnet/UI.Blazor.App/Components/RightPanel/RightPanelChatInfo.razor` — toggle pattern (Summarize)
- `src/dotnet/UI.Blazor.App/Components/ChatSettings/EditChatTypeModalPage.razor` — ToggleEdit pattern
- `src/dotnet/UI.Blazor.App/Components/ChatSettings/EditChatMembersModalPage.razor` — member add/remove (key rotation hook)

### Message editor & restrictions:
- `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditor.razor` — Post(), attachment/audio handling
- `src/dotnet/UI.Blazor.App/Components/ChatMessageEditor/ChatMessageEditorMenu.razor` — editor menu (attachment, forward)
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/MessageHoverMenu.razor` — message context menu (forward option)

### Chat list & notifications:
- `src/dotnet/UI.Blazor.App/Components/ChatList/` — last message preview
- `src/dotnet/Notifications.Service/` — notification text for encrypted messages

### DB & serialization:
- `src/dotnet/Api/Chat/ChatEntry.cs` — Content field, MemoryPackOrder values
- `src/dotnet/Chat.Service/Db/DbChatEntry.cs` — DB entity
- `src/dotnet/Chat.Service/Db/ChatDbContext.cs` — model builder pattern
- `docs/api-index.md` — type catalog

### Member management (for key rotation):
- `src/dotnet/Api.Contracts/Chat/IAuthors.cs` — Authors_Join, Authors_Invite, Authors_Exclude
- `src/dotnet/Chat.Service/AuthorsBackend.cs` — member change events

### Module registration:
- `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs` — client service registration

---

## Verification

### Unit tests:
- `E2ECrypto` roundtrip: encrypt → decrypt = original
- Key pair generation and group key wrap/unwrap roundtrip
- Encrypted format parsing
- Edge cases: empty content, unicode, long messages

### Integration tests:
- Enable E2E on chat → `IsE2EEncrypted` persisted
- Send message to E2E chat → `Content` stored as `e2e:1:...` format
- Read own E2E message → decrypted correctly
- Non-text message rejected in E2E chat (server-side validation)
- Key rotation on member change

### Manual testing:
- Toggle E2E in right panel → lock icon appears
- Send text message → appears normally (decrypted client-side)
- Audio/attachment buttons disabled in E2E chat
- New member joins → cannot read old messages
- DB inspection: verify `Content` column contains ciphertext, not plaintext
