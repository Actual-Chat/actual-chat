# Walkie-Talkie Heard Receipts (Sub-Project D) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a walkie-talkie listener's device actually starts playing a voice message, record a per-user `Heard` watermark server-side so the sender's existing transient "Unread" label clears — with zero new UI.

**Architecture:** A new `ChatPositionKind.Heard` rides the existing user-sharded `ChatPositions` machinery and feeds the existing `ReadPositionsStat` bridge (stat becomes max(read, heard) per user). The write path is a server-side ack: the client calls a new `ILiveAudioStreams.ReportPlayback(session, chatId, streamId, entryId?)` when a track starts rendering; the server validates the chat is continuously-listened (walkie-talkie armed), resolves streamId → entry when the client has no entry id (live streams), and issues `ChatPositionsBackend_Set(kind: Heard)`. Spec: `docs/superpowers/specs/2026-07-20-walkie-talkie-heard-receipts-design.md`.

**Tech Stack:** .NET / ActualLab.Fusion compute services + RPC, EF Core (Npgsql) migrations, xUnit + AwesomeAssertions integration tests with `AppHost` fixtures.

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any code.** No `Async` suffix on async methods; no XML docs on members; comments only where the code cannot express a constraint; mirror surrounding brace/naming style.
- Branch: `feat/walkie-talkie-push`. Commit per task; **never push**.
- Build with `dotnet build ActualChat.CI.slnf` (never the full `.sln` — MAUI workloads are absent).
- `ChatPositionKind.Heard` MUST be appended as the third member (value 2) — never inserted before `View` (wire format is numeric).
- The listener's `Read` position, unread counters, and notification behavior must stay untouched: `ReadPositionChangedEvent` remains Read-only.
- Sender-side UI: **no changes anywhere** — visibility comes solely from the existing `ReadPositionsStat` invalidation.
- `.superpowers/sdd/progress.md` is gitignored — update it, but never `git add` it.

## Reuse

**Existing abstractions reused** (verified against source, 2026-07-20):

| Abstraction | Location | How it's used |
|---|---|---|
| `ChatPositionKind` / `ChatPosition` / `IChatPositionsBackend` / `ChatPositionsBackend.OnSet` / `DbChatPosition` | `src/dotnet/Api/Users/ChatPosition.cs`, `src/dotnet/Users.Contracts/IChatPositionsBackend.cs`, `src/dotnet/Users.Service/ChatPositionsBackend.cs` | Extended with the `Heard` enum member only; storage (string key `"{userId} {chatId}:{kind}"`), sharding, forward-only logic, Fusion invalidation all inherited. No EF migration needed (kind is in the string PK + a plain int column). |
| `ChatsBackend_UpdateReadPositionsStat` + `DbReadPositionsStat` + `ReadPositionsStat.HasReadByAnotherAuthor` | `src/dotnet/Chat.Contracts/IChatsBackend.cs:362`, `src/dotnet/Chat.Service/ChatsBackend.cs:1833` | Untouched — per-user forward-only max already gives max(read, heard); `Heard` just becomes a second enqueue source. |
| `ILiveAudioStreams.ReportAudioLatency` pattern | `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs:53`, `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs:105` | `ReportPlayback` is a sibling plain (non-compute) RPC method on the same service, called from players via `Hub.LiveAudioStreams`. |
| `IsArmedForWalkieTalkie` predicate (sub-project A) | `src/dotnet/Notifications.Service/NotificationsBackend.cs:1024` | Promoted to a shared `IServerKvasBackend.IsWalkieTalkieArmed` extension in `Users.Contracts`; NotificationsBackend refactored to call it. |
| `LiveAudioStreamInfo.EntryId` / `ReplayStreamMuxer` | `src/dotnet/Api/Live/LiveAudioStreamInfo.cs`, `src/dotnet/Streaming.Service/Services/ReplayStreamMuxer.cs:143` | Already populate EntryId + StreamId for replay streams; unchanged. |
| `Playback` track state machine | `src/dotnet/Api/MediaPlayback/Playback.cs:163-201` | The `!prev.IsStarted && state.IsStarted` edge (driven by the JS `AudioTrackPlayer.OnPlaying` callback = actual rendering) gains a dedicated `OnTrackStarted` event. |
| `ChatAudioUI.GetChatsYouNeedToKeepListeningTo` + `UserSettingsUI.ChatUserSettings` | `src/dotnet/UI.Blazor.App/Services/ChatAudioUI.cs:105`, `src/dotnet/Api/Users/UserSettingsUIExt.cs` | Client-side ack gate (traffic saving; the server check is authoritative). |
| Test harness: `SharedAppHostTestBase<AppHostFixture>` + `StreamingCollection`, `ComputedTest.When`, kvas `.Update` arming helper | `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs`, `LiveAudioBackendShardMigrationTest.cs:35`, `tests/Notifications.IntegrationTests/WalkieTalkiePushTest.cs:232-237` | Patterns copied for the new `ReportPlaybackTest`. |

**Reusability of new components** (local vs shared placement decided):

- `IsWalkieTalkieArmed` → **shared**, `Users.Contracts/ServerKvasBackendExt.cs` (used by Notifications.Service today and Streaming.Service now; both reference Users.Contracts).
- `Playback.OnTrackStarted` → **shared**, `Api/MediaPlayback/Playback.cs` (generic playback concept, not chat-specific).
- `FindEntryIdByAudioStreamId` → **shared**, `IChatsBackend` (generic entry lookup, reusable by any backend service; nearest precedent is the `ContentStreamId` query in `TranslationsBackend.cs:391`).
- `ReportPlayback` → `ILiveAudioStreams` (Api.Contracts) — the API surface it extends; no more-shared home applies.
- `ChatAudioTrackInfo.StreamId`/`EntryId` → stays in `UI.Blazor.App` (type is UI-app-specific already).
- No fit found for: an existing entry-by-audio-streamId query (searched `ChatsBackend`, `TranslationsBackend`, live registry — none exists; `ILiveAudioBackend`'s registry carries no EntryId for live streams), hence Task 3 adds one.

---

### Task 1: `ChatPositionKind.Heard` + backend threading

**Files:**
- Modify: `src/dotnet/Api/Users/ChatPosition.cs:8`
- Modify: `src/dotnet/Users.Service/ChatPositionsBackend.cs:32-106` (OnSet)
- Test: `tests/Users.UnitTests/UserCommandSerializationTest.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ChatPositionKind.Heard` (= 2). `ChatPositionsBackend_Set(userId, chatId, ChatPositionKind.Heard, new ChatPosition(entryLid))` becomes forward-only and enqueues `ChatsBackend_UpdateReadPositionsStat(chatId, userId, entryLid)` on change. Task 4's server handler issues exactly this command.

- [ ] **Step 1: Write the failing serialization tests**

In `tests/Users.UnitTests/UserCommandSerializationTest.cs`, next to the existing `ChatPositions_Set_Basic` (line ~107) and `ChatPositionsBackend_Set_Basic` (line ~212), add two facts. Mirror the exact constants/helpers those two facts use (`TestSession`, `TestChatId`, `AssertPassesThroughAllSerializers`, and for the backend command whatever UserId constant the sibling uses):

```csharp
[Fact]
public void ChatPositions_Set_Heard()
{
    var position = new ChatPosition(42, "origin");
    var cmd = new ChatPositions_Set(TestSession, TestChatId, ChatPositionKind.Heard, position);
    cmd.AssertPassesThroughAllSerializers();
}
```

and a `ChatPositionsBackend_Set_Heard` fact that is a copy of `ChatPositionsBackend_Set_Basic` with `ChatPositionKind.Heard` as the kind argument.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserCommandSerializationTest" 2>&1 | tail -5`
Expected: build FAILS with `'ChatPositionKind' does not contain a definition for 'Heard'`.

- [ ] **Step 3: Add the enum member**

`src/dotnet/Api/Users/ChatPosition.cs:8`:

```csharp
public enum ChatPositionKind { Read = 0, View, Heard };
```

- [ ] **Step 4: Thread `Heard` through `ChatPositionsBackend.OnSet`**

In `src/dotnet/Users.Service/ChatPositionsBackend.cs`, two edits:

Edit A — the update gate (currently `else if (force || kind != ChatPositionKind.Read || position.EntryLid > dbChatPosition.EntryLid)`), making `Heard` forward-only like `Read` while `View` stays overwrite-always:

```csharp
        else if (force || kind == ChatPositionKind.View || position.EntryLid > dbChatPosition.EntryLid) {
```

Edit B — the post-write block (currently `if (kind == ChatPositionKind.Read && hasChanges) { ... }`). `Heard` must enqueue the stat update but must NOT emit `ReadPositionChangedEvent` (notification reconciliation stays Read-driven). Replace the block with:

```csharp
        if (hasChanges && kind is ChatPositionKind.Read or ChatPositionKind.Heard) {
            if (kind == ChatPositionKind.Read)
                context.Operation
                    .AddEvent(new ReadPositionChangedEvent(userId, chatId, position.EntryLid))
                    .SetDelayBy(TimeSpan.Zero, Constants.Notification.ReadReconcileWindow, $"ReadPosChanged:{userId.Value}:{chatId.Value}");

            var stat = await ChatsBackend.GetReadPositionsStat(chatId, cancellationToken).ConfigureAwait(false);
            var needUpdateStat = stat is null || MightUpdateStat(stat, userId, position.EntryLid);
            if (needUpdateStat)
                await Queues.Enqueue(new ChatsBackend_UpdateReadPositionsStat(chatId, userId, position.EntryLid), cancellationToken).ConfigureAwait(false);
        }
```

Keep the `long.MaxValue` sentinel guard near the top of OnSet gated on `kind == ChatPositionKind.Read` exactly as it is — `Heard` never sends the sentinel.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Users.UnitTests/Users.UnitTests.csproj --filter "FullyQualifiedName~UserCommandSerializationTest" 2>&1 | tail -5`
Expected: PASS (all facts in the class, including the two new ones).

- [ ] **Step 6: Build the touched services**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Users/ChatPosition.cs src/dotnet/Users.Service/ChatPositionsBackend.cs tests/Users.UnitTests/UserCommandSerializationTest.cs
git commit -m "feat(users): ChatPositionKind.Heard - forward-only watermark feeding ReadPositionsStat"
```

---

### Task 2: Shared `IsWalkieTalkieArmed` predicate

**Files:**
- Modify: `src/dotnet/Users.Contracts/ServerKvasBackendExt.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs:1024-1037`

**Interfaces:**
- Consumes: `IServerKvasBackend.ForUser` (`ServerKvasBackendExt.cs`), `UserScopedKvasBackendExt.UserListeningSettings()` / `.ChatUserSettings(chatId)` (`src/dotnet/Users.Contracts/UserScopedKvasBackendExt.cs:10,22`).
- Produces: `Task<bool> IsWalkieTalkieArmed(this IServerKvasBackend, UserId userId, ChatId chatId, CancellationToken)` — Task 4's server handler calls it.

- [ ] **Step 1: Add the extension**

In `src/dotnet/Users.Contracts/ServerKvasBackendExt.cs` add (namespace/usings: the file already lives beside `UserScopedKvasBackendExt.cs`; add `using ActualChat.Chat;` and/or `using ActualChat.Users;` only if the compiler asks):

```csharp
public static async Task<bool> IsWalkieTalkieArmed(
    this IServerKvasBackend serverKvasBackend,
    UserId userId,
    ChatId chatId,
    CancellationToken cancellationToken)
{
    var kvas = serverKvasBackend.ForUser(userId);
    var alwaysListened = await kvas.UserListeningSettings()
        .Get(x => x.AlwaysListenedChatIds, cancellationToken)
        .ConfigureAwait(false);
    if (alwaysListened.Contains(chatId))
        return true;

    var listeningMode = await kvas.ChatUserSettings(chatId)
        .Get(x => x.ListeningMode, cancellationToken)
        .ConfigureAwait(false);
    return listeningMode == ListeningMode.Forever;
}
```

The body is verbatim the current private `NotificationsBackend.IsArmedForWalkieTalkie` (lines 1024-1037) with `ServerKvasBackend` replaced by the extension receiver.

- [ ] **Step 2: Refactor NotificationsBackend to delegate**

Replace the body of `IsArmedForWalkieTalkie` in `src/dotnet/Notifications.Service/NotificationsBackend.cs` with:

```csharp
private Task<bool> IsArmedForWalkieTalkie(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    => ServerKvasBackend.IsWalkieTalkieArmed(userId, chatId, cancellationToken);
```

- [ ] **Step 3: Build + run the walkie-talkie regression suite**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -5`
Expected: PASS (same count as on the base commit; this suite passed 91/91-wide at C-completion).

- [ ] **Step 4: Commit**

```bash
git add src/dotnet/Users.Contracts/ServerKvasBackendExt.cs src/dotnet/Notifications.Service/NotificationsBackend.cs
git commit -m "refactor(users): promote walkie-talkie armed predicate to shared IServerKvasBackend extension"
```

---

### Task 3: `FindEntryIdByAudioStreamId` backend query + `audio_id` index

**Files:**
- Modify: `src/dotnet/Chat.Contracts/IChatsBackend.cs`
- Modify: `src/dotnet/Chat.Service/ChatsBackend.cs`
- Modify: `src/dotnet/Chat.Service/Db/ChatDbContext.cs:60` (next to the ContentStreamId index)
- Create: EF migration in `src/dotnet/Chat.Service.Migration/Migrations/` (generated)
- Test: `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs` (new file)

**Interfaces:**
- Consumes: `DbChatEntry.AudioId` (holds `ChatEntryAudio.StreamId` while an entry is streaming, a `MediaId` after finalize — `src/dotnet/Chat.Service/Db/DbChatEntry.cs:238`), `ChatsBackend_ChangeEntry` + `ChatEntryDiff.Audio` for test setup (pattern: `AudioStreamingBackend.ProcessAudio.cs:535-559`).
- Produces: `Task<ChatEntryId> FindEntryIdByAudioStreamId(ChatId chatId, string audioStreamId, CancellationToken cancellationToken)` on `IChatsBackend` — plain (non-`[ComputeMethod]`) method; returns `default` (`IsNone`) when no live entry matches. Task 4 calls it with retry.

- [ ] **Step 1: Write the failing test**

Create `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs`:

```csharp
using ActualChat.Chat;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ReportPlaybackTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task FindEntryIdByAudioStreamId_ResolvesStreamingEntry()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var (chat, entry, streamId) = await CreateChatWithStreamingAudioEntry(session, "FindEntryTest");

        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var foundId = await chatsBackend.FindEntryIdByAudioStreamId(chat.Id, streamId, CancellationToken.None);
        foundId.Should().Be(entry.Id);

        var missingId = await chatsBackend.FindEntryIdByAudioStreamId(chat.Id, "no-such-stream", CancellationToken.None);
        missingId.IsNone.Should().BeTrue();
    }

    private async Task<(Chat.Chat Chat, ChatEntry Entry, string StreamId)> CreateChatWithStreamingAudioEntry(
        Session session, string title)
    {
        var services = AppHost.Services;
        var commander = services.Commander();
        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = title,
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, chat.Id, CancellationToken.None);
        author.Require();

        var streamId = $"test-audio-{Guid.NewGuid():N}";
        var entryId = ChatEntryId.New(chat.Id, 0);
        var entry = await commander.Call(new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "",
                Audio = new ChatEntryAudio { StreamId = streamId },
                BeginsAt = services.Clocks().SystemClock.Now,
            })));
        return (chat, entry, streamId);
    }
}
```

If the compiler flags a helper/using mismatch (e.g. `Clocks()` extension namespace), mirror the resolutions used in the sibling `tests/Streaming.IntegrationTests/LiveAudioStreamsTest.cs` — the fixture pattern above is copied from it.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -5`
Expected: build FAILS with `'IChatsBackend' does not contain a definition for 'FindEntryIdByAudioStreamId'`.

- [ ] **Step 3: Add the interface method**

In `src/dotnet/Chat.Contracts/IChatsBackend.cs`, next to `GetReadPositionsStat` (line ~136), add a plain method (deliberately NOT `[ComputeMethod]` — results flip as streaming entries appear/finalize and the caller retries instead of depending on invalidation):

```csharp
Task<ChatEntryId> FindEntryIdByAudioStreamId(ChatId chatId, string audioStreamId, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement the query**

In `src/dotnet/Chat.Service/ChatsBackend.cs`, near the other read queries:

```csharp
public async Task<ChatEntryId> FindEntryIdByAudioStreamId(
    ChatId chatId, string audioStreamId, CancellationToken cancellationToken)
{
    if (audioStreamId.IsNullOrEmpty())
        return default;

    var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
    await using var _ = dbContext.ConfigureAwait(false);

    var sid = await dbContext.ChatEntries
        .Where(e => e.Kind == 0 && e.AudioId == audioStreamId)
        .Select(e => e.Id)
        .FirstOrDefaultAsync(cancellationToken)
        .ConfigureAwait(false);
    if (sid == null)
        return default;

    var entryId = ChatEntryId.Parse(sid);
    return entryId.ChatId == chatId ? entryId : default;
}
```

The `e.Kind == 0` comparison style mirrors `TranslationsBackend.cs:391` (same table, same column typing).

- [ ] **Step 5: Add the filtered index**

In `src/dotnet/Chat.Service/Db/ChatDbContext.cs`, right after the ContentStreamId index (line 60):

```csharp
chatEntry.HasIndex(e => e.AudioId).HasFilter("\"kind\" = 0 AND \"audio_id\" IS NOT NULL");
```

- [ ] **Step 6: Generate the migration**

```bash
dotnet tool install --global dotnet-ef --version 9.0.1 2>/dev/null; \
dotnet ef migrations add Add_ChatEntry_AudioId_Index \
    --project src/dotnet/Chat.Service.Migration/Chat.Service.Migration.csproj
```

(`ChatDbContextContextFactory.cs` in that project provides the design-time context; no running DB needed.)
Expected: a new `..._Add_ChatEntry_AudioId_Index.cs` + `.Designer.cs` pair and an updated `ChatDbContextModelSnapshot.cs`. Open the migration and verify `Up` contains exactly one `CreateIndex` on table `chat_entries`, column `audio_id`, with filter `"kind" = 0 AND "audio_id" IS NOT NULL` (name `ix_chat_entries_audio_id`), and `Down` drops it. If it contains anything else, the snapshot drifted — delete the generated files and investigate before retrying.

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -5`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Chat.Contracts/IChatsBackend.cs src/dotnet/Chat.Service/ChatsBackend.cs \
    src/dotnet/Chat.Service/Db/ChatDbContext.cs src/dotnet/Chat.Service.Migration/Migrations/ \
    tests/Streaming.IntegrationTests/ReportPlaybackTest.cs
git commit -m "feat(chat): FindEntryIdByAudioStreamId backend query + filtered audio_id index"
```

---

### Task 4: `ReportPlayback` API + server handler

**Files:**
- Modify: `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs` (after `ReportAudioLatency`, line ~53)
- Modify: `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`
- Test: `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs` (extend Task 3's file)

**Interfaces:**
- Consumes: `ChatPositionKind.Heard` + `ChatPositionsBackend_Set` (Task 1), `IsWalkieTalkieArmed` (Task 2), `FindEntryIdByAudioStreamId` (Task 3).
- Produces: `Task ReportPlayback(Session session, ChatId chatId, string streamId, ChatEntryId? entryId, CancellationToken cancellationToken)` on `ILiveAudioStreams` — Task 5's client hook calls it. Silent no-op (no throw) on: no read-audio permission, not walkie-talkie-armed, unresolvable stream, foreign-chat entryId.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Streaming.IntegrationTests/ReportPlaybackTest.cs` (reusing Task 3's `CreateChatWithStreamingAudioEntry` helper; `ComputedTest.When` usage mirrors `LiveAudioBackendShardMigrationTest.cs:35`):

```csharp
    [Fact]
    public async Task ReportPlayback_EntryIdPath_SetsHeardAndStat_LeavesReadUntouched()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Heidi"));
        var (chat, entry, _) = await CreateChatWithStreamingAudioEntry(session, "HeardEntryIdTest");
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry.Id.LocalId);

        var read = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Read, CancellationToken.None);
        read.EntryLid.Should().Be(0);

        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        await ComputedTest.When(async ct => {
            var stat = await chatsBackend.GetReadPositionsStat(chat.Id, ct);
            stat.Should().NotBeNull();
            stat!.TopReadPositions.Should()
                .Contain(p => p.UserId == account.Id && p.EntryLid == entry.Id.LocalId);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReportPlayback_StreamIdPath_ResolvesEntry()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Ivan"));
        var (chat, entry, streamId) = await CreateChatWithStreamingAudioEntry(session, "HeardStreamIdTest");
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, streamId, null, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry.Id.LocalId);
    }

    [Fact]
    public async Task ReportPlayback_NotArmed_DoesNothing()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Judy"));
        var (chat, entry, _) = await CreateChatWithStreamingAudioEntry(session, "HeardNotArmedTest");

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(0);
    }

    [Fact]
    public async Task ReportPlayback_IsForwardOnly()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Kate"));
        var (chat, entry1, _) = await CreateChatWithStreamingAudioEntry(session, "HeardForwardOnlyTest");
        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, chat.Id, CancellationToken.None);
        author.Require();
        var entry2 = await services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chat.Id, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "",
                Audio = new ChatEntryAudio { StreamId = $"test-audio-{Guid.NewGuid():N}" },
                BeginsAt = services.Clocks().SystemClock.Now,
            })));
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry2.Id, CancellationToken.None);
        await liveStreams.ReportPlayback(session, chat.Id, "", entry1.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry2.Id.LocalId);
    }

    private Task Arm(UserId userId, ChatId chatId)
        => AppHost.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(userId).ChatUserSettings(chatId)
            .Update(x => x with { ListeningMode = ListeningMode.Forever });
```

Add the usings the compiler asks for (`ActualChat.Users` for `ChatPositionKind`/`IChatPositionsBackend`/`IServerKvasBackend`/`ListeningMode`; the arming helper mirrors `WalkieTalkiePushTest.cs:235-237`). If `SignIn` returns a type without `Id`, mirror how `WalkieTalkiePushTest` obtains the signed-in user's `UserId`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -5`
Expected: build FAILS with `'ILiveAudioStreams' does not contain a definition for 'ReportPlayback'`.

- [ ] **Step 3: Add the interface method**

In `src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs`, directly after `ReportAudioLatency` (no attribute — plain RPC method, same as its sibling):

```csharp
    Task ReportPlayback(
        Session session, ChatId chatId, string streamId, ChatEntryId? entryId,
        CancellationToken cancellationToken);
```

- [ ] **Step 4: Implement the handler**

In `src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs`, add lazy DI properties next to the existing ones (same `field ??=` idiom):

```csharp
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ICommander Commander => field ??= Services.Commander();
```

and the methods (non-virtual, next to `ReportAudioLatency`):

```csharp
    public async Task ReportPlayback(
        Session session, ChatId chatId, string streamId, ChatEntryId? entryId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.Has(ChatPermissions.ReadAudio))
            return;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!await ServerKvasBackend.IsWalkieTalkieArmed(account.Id, chatId, cancellationToken).ConfigureAwait(false))
            return;

        var resolvedEntryId = entryId is { IsNone: false } eid
            ? eid
            : await ResolveEntryId(chatId, streamId, cancellationToken).ConfigureAwait(false);
        if (resolvedEntryId.IsNone || resolvedEntryId.ChatId != chatId)
            return;

        var command = new ChatPositionsBackend_Set(
            account.Id, chatId, ChatPositionKind.Heard, new ChatPosition(resolvedEntryId.LocalId));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatEntryId> ResolveEntryId(
        ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        if (streamId.IsNullOrEmpty())
            return default;

        for (var attempt = 0;; attempt++) {
            var entryId = await ChatsBackend.FindEntryIdByAudioStreamId(chatId, streamId, cancellationToken)
                .ConfigureAwait(false);
            if (!entryId.IsNone || attempt >= 5)
                return entryId;

            // Live acks can arrive before the transcriber creates the text entry
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }
```

Add usings for `ActualChat.Users` (and `ActualChat.Chat` if not present). Note the permission check mirrors `List` (`chat.Rules.Has(ChatPermissions.ReadAudio)`), returning silently instead of throwing — the ack is fire-and-forget.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -5`
Expected: PASS, 5 tests (incl. Task 3's).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api.Contracts/Streaming/ILiveAudioStreams.cs \
    src/dotnet/Streaming.Service/Services/LiveAudioStreams.cs \
    tests/Streaming.IntegrationTests/ReportPlaybackTest.cs
git commit -m "feat(streaming): ReportPlayback ack - resolves entry, writes Heard position for armed chats"
```

---

### Task 5: Client wiring — render-start hook → `ReportPlayback`

**Files:**
- Modify: `src/dotnet/Api/MediaPlayback/Playback.cs` (event at line ~29, edge at lines 182-186)
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatAudioTrackInfo.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatPlayer.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatListeningPlayer.cs:180-184`
- Modify: `src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayPlayer.cs:136-148`

**Interfaces:**
- Consumes: `ReportPlayback` (Task 4), `ChatAudioUI.GetChatsYouNeedToKeepListeningTo`, `UserSettingsUI.ChatUserSettings(chatId).Get(x => x.ListeningMode, ct)`, `Hub.LiveAudioStreams`, `Hub.PlaybackFactory` / `StopToken` (ProcessorBase).
- Produces: `Playback.OnTrackStarted` event (`Action<TrackInfo, PlayerState>?`, fires once per track, on actual render start); `ChatAudioTrackInfo.EntryId` (`ChatEntryId?`, get-only) and `ChatAudioTrackInfo.StreamId` (`string`, init, default `""`).

There are no .NET unit tests for the player pipeline (existing coverage is `ChatReplayPlayerTest` integration + manual); this task's automated verification is build + replay-player regression, with behavior covered end-to-end by Task 4's server tests and the final manual two-device pass.

- [ ] **Step 1: Add `Playback.OnTrackStarted`**

In `src/dotnet/Api/MediaPlayback/Playback.cs`, next to `OnTrackPlayingChanged` (line 29):

```csharp
    public event Action<TrackInfo, PlayerState>? OnTrackStarted;
```

Then in `TrackPlayerStateChanged`, replace the started-edge block (lines 182-186):

```csharp
        if (!prev.IsStarted && state.IsStarted)
            lock (_stateUpdateLock) {
                _playingTracks.Value = _playingTracks.Value.Insert(0, (trackInfo, state));
                _isPlaying.Value = true;
            }
```

with:

```csharp
        if (!prev.IsStarted && state.IsStarted) {
            lock (_stateUpdateLock) {
                _playingTracks.Value = _playingTracks.Value.Insert(0, (trackInfo, state));
                _isPlaying.Value = true;
            }
            try {
                OnTrackStarted?.Invoke(trackInfo, state);
            }
            catch (Exception ex) {
                _log.LogError(ex, $"Unhandled exception in {nameof(OnTrackStarted)}");
            }
        }
```

(`IsStarted` flips only from the JS `AudioTrackPlayer.OnPlaying` callback, so this edge is actual rendering start; it fires at most once per track.)

- [ ] **Step 2: Carry ids on `ChatAudioTrackInfo`**

In `src/dotnet/UI.Blazor.App/Services/Playback/ChatAudioTrackInfo.cs` add two members and set `EntryId` in both constructors:

```csharp
    public ChatEntryId? EntryId { get; }
    public string StreamId { get; init; } = "";
```

Entry-based constructor body gains `EntryId = audioEntry.Id;`; the RTC constructor body gains `EntryId = entryId;`.

- [ ] **Step 3: Populate `StreamId` at both construction sites**

`ChatListeningPlayer.EnqueueAudioSource` (line ~180) — add `StreamId = streamInfo.StreamId,` to the initializer:

```csharp
        var trackInfo = new ChatAudioTrackInfo(ChatId, null, chat, author) {
            RecordedAt = streamInfo.BeginsAt + skipTo,
            SourceRecordedAt = sourceRecordedAt,
            TargetBufferSize = targetBufferSize,
            StreamId = streamInfo.StreamId,
        };
```

`ChatReplayPlayer.OnStreamStarted` (lines ~136-148) — add `StreamId = streamInfo.StreamId,` to **both** initializers (the entry-based and the RTC-ctor branch).

- [ ] **Step 4: Hook + gate + ack in `ChatPlayer`**

In `src/dotnet/UI.Blazor.App/Services/Playback/ChatPlayer.cs`:

Add a lazy service property next to the other protected accessors:

```csharp
    protected UserSettingsUI UserSettingsUI => field ??= Hub.Services.GetRequiredService<UserSettingsUI>();
```

In the constructor, right after `Playback = Hub.PlaybackFactory.Create();`:

```csharp
        Playback.OnTrackStarted += OnPlaybackTrackStarted;
```

Add the private methods:

```csharp
    private void OnPlaybackTrackStarted(TrackInfo trackInfo, PlayerState state)
    {
        if (trackInfo is not ChatAudioTrackInfo info)
            return;
        if (info.StreamId.IsNullOrEmpty() && info.EntryId == null)
            return;

        _ = ReportPlayback(info, StopToken);
    }

    private async Task ReportPlayback(ChatAudioTrackInfo info, CancellationToken cancellationToken)
    {
        try {
            var alwaysListenedChatIds = await ChatAudioUI.GetChatsYouNeedToKeepListeningTo(cancellationToken)
                .ConfigureAwait(false);
            if (!alwaysListenedChatIds.Contains(ChatId)) {
                var listeningMode = await UserSettingsUI.ChatUserSettings(ChatId)
                    .Get(x => x.ListeningMode, cancellationToken)
                    .ConfigureAwait(false);
                if (listeningMode != ListeningMode.Forever)
                    return;
            }
            await Hub.LiveAudioStreams
                .ReportPlayback(Session, ChatId, info.StreamId, info.EntryId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "ReportPlayback failed in chat #{ChatId}", ChatId);
        }
    }
```

Add usings the compiler asks for (`ActualChat.Users` for `ListeningMode`/`UserSettingsUI` accessors; `Microsoft.Extensions.DependencyInjection` if `GetRequiredService` is unresolved). No unsubscribe is needed — `Playback` is owned by the player and disposed with it in `DisposeAsyncCore`.

The client gate matches the server's `IsWalkieTalkieArmed` predicate (always-listened set OR per-chat `ListeningMode.Forever`); it only saves traffic — the server check stays authoritative. Both wake paths are covered automatically: a headless walkie-talkie wake starts `ChatReplayPlayer` (fresh replay) or `ChatListeningPlayer` (stale/foreground restore), and both funnel every track through `Playback`.

- [ ] **Step 5: Build + replay regression**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~ChatReplayPlayerTest" 2>&1 | tail -5`
Expected: PASS (regression only — the fixture has no JS audio, so the new hook stays dormant there).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/MediaPlayback/Playback.cs \
    src/dotnet/UI.Blazor.App/Services/Playback/ChatAudioTrackInfo.cs \
    src/dotnet/UI.Blazor.App/Services/Playback/ChatPlayer.cs \
    src/dotnet/UI.Blazor.App/Services/Playback/ChatListeningPlayer.cs \
    src/dotnet/UI.Blazor.App/Services/Playback/ChatReplayPlayer.cs
git commit -m "feat(audio-ui): report playback start of walkie-talkie tracks as heard"
```

---

### Task 6: Final verification

**Files:**
- Modify: `docs/superpowers/specs/2026-07-20-walkie-talkie-heard-receipts-design.md` (status line only)
- Modify (not committed): `.superpowers/sdd/progress.md`

- [ ] **Step 1: Full build**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -3`
Expected: `0 Error(s)`.

- [ ] **Step 2: Full affected test sweep**

```bash
dotnet test tests/Users.UnitTests/Users.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj 2>&1 | tail -3
dotnet test tests/Streaming.IntegrationTests/Streaming.IntegrationTests.csproj --filter "FullyQualifiedName~ReportPlaybackTest" 2>&1 | tail -3
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~WalkieTalkiePushTest" 2>&1 | tail -3
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~ApiEvolutionTest" 2>&1 | tail -3
```

Expected: all PASS (ApiEvolutionTest guards `ChatPosition` wire compat).

- [ ] **Step 3: Update spec status + ledger**

- Spec: change `Status: Draft design, pending review` → `Status: Implemented`.
- `.superpowers/sdd/progress.md`: append D-task completion lines (commit ranges + review outcomes) following the existing format. Do NOT `git add` it.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-07-20-walkie-talkie-heard-receipts-design.md
git commit -m "docs: mark heard-receipts spec implemented"
```

- [ ] **Step 5: Record the manual verification item for the host**

Report to the user (do not attempt on this machine): two-device walkie-talkie pass — speak on device A, hear headlessly on device B (armed chat, app backgrounded), then watch A's transient "Unread" label clear without B ever opening the chat.
