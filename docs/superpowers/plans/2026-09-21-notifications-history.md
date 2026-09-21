# Notifications History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give agents a way to ask "was I addressed?" — a per-user notification log in the Notifications DB, a plain RPC read method on `INotifications`, and an MCP tool `list_notifications`, filterable by kind and paged by a cursor.

**Architecture:** `NotificationsBackend` gains an `[EventHandler]` for the existing `UserNotifiedEvent` (the same event web hooks consume) that appends one row per addressed notification to a new `NotificationHistory` table; a pruner deletes rows older than 30 days. `NotificationsService.ListHistory` resolves the session's account and queries the backend; `McpNotificationTools.list_notifications` wraps it and re-resolves the anchored message through the session.

**Tech Stack:** .NET 11 preview, ActualLab.Fusion (RPC, commands, events, `ShardedDbServiceBase`), EF Core + Npgsql (migrations via `dotnet ef`), MessagePack + DataContract records, ModelContextProtocol server, xUnit + FluentAssertions integration tests over `SharedAppHostTestBase`.

**Spec:** `docs/superpowers/specs/2026-09-21-notifications-history-design.md`

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing any C#. No `Async` suffix, no XML docs on members, mixed brace style (Allman for types/methods, K&R everywhere else), control-flow statements on their own line followed by a blank line, `.ConfigureAwait(false)` in service code, bool names prefixed `is`/`has`, `sealed` unless proxied (`*Backend`, `*Service` stay unsealed with `virtual` members).
- Comments only where a skimming senior developer would otherwise miss an invariant. Do not restate code.
- Every serializable record carries `[DataContract, MessagePackObject]` and every serialized member `[DataMember(Order = N), Key(N)]`. Never renumber keys.
- Logged kinds: `Mention`, `Reply`, `Reaction`, `Attention`, `Thread`, `Invitation`, `Conversation`, `IncomingCall`. `Message` and `SpeechStarted` are never logged.
- Limits: `HistoryDefaultLimit = 64`, `HistoryMaxLimit = 256`, `HistoryRetention = 30 days`.
- Build per test project (`dotnet build tests/<Project>/<Project>.csproj`); the `*.CI.slnf` file is stale locally. Tests run against the host's PostgreSQL/NATS/Redis, which are already running.
- Commit after every task with a conventional-commit subject and the attribution trailer `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`. Never push.
- Test names: PascalCase with `Should`, AAA comments in lowercase (`// arrange`, `// act`, `// assert`), FluentAssertions with a `because` when the failure is not self-explanatory.

---

### Task 1: Contracts, constants and the API surface

**Files:**
- Create: `src/dotnet/Api/Notifications/NotificationHistoryItem.cs`
- Create: `src/dotnet/Api/Notifications/NotificationHistoryQuery.cs`
- Modify: `src/dotnet/Api/Constants.cs` (the `Notification` nested class, after `ActiveReaderGrace` around line 511)
- Modify: `src/dotnet/Api.Contracts/Notifications/INotifications.cs`
- Modify: `src/dotnet/Notifications.Contracts/INotificationsBackend.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsService.cs`
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs` (stubs only, filled in Task 3)
- Test: `tests/Notifications.IntegrationTests/NotificationHistoryItemTest.cs`

**Interfaces:**
- Produces: `NotificationHistoryItem(long Seq, NotificationKind Kind)` with `SentAt`, `ChatId?`, `EntryId?`, `AuthorId?`, `Title`, `Text` and `static bool IsLoggedKind(NotificationKind)`; `NotificationHistoryQuery { Kinds, AfterSeq, Limit, IsNewestFirst }`; `INotifications.ListHistory(Session, NotificationHistoryQuery, CancellationToken)`; `INotificationsBackend.ListHistory(UserId, NotificationHistoryQuery, CancellationToken)` and `INotificationsBackend.OnUserNotifiedEvent(UserNotifiedEvent, CancellationToken)`; `Constants.Notification.HistoryDefaultLimit / HistoryMaxLimit / HistoryRetention`.

- [ ] **Step 1: Write the failing test**

Create `tests/Notifications.IntegrationTests/NotificationHistoryItemTest.cs`:

```csharp
namespace ActualChat.Notifications.IntegrationTests;

public class NotificationHistoryItemTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(NotificationKind.Mention, true)]
    [InlineData(NotificationKind.Reply, true)]
    [InlineData(NotificationKind.Reaction, true)]
    [InlineData(NotificationKind.Attention, true)]
    [InlineData(NotificationKind.Thread, true)]
    [InlineData(NotificationKind.Invitation, true)]
    [InlineData(NotificationKind.Conversation, true)]
    [InlineData(NotificationKind.IncomingCall, true)]
    [InlineData(NotificationKind.Message, false)]
    [InlineData(NotificationKind.SpeechStarted, false)]
    [InlineData(NotificationKind.None, false)]
    [InlineData(NotificationKind.Invalid, false)]
    public void IsLoggedKindShouldMatchTheSpec(NotificationKind kind, bool isLogged)
        => NotificationHistoryItem.IsLoggedKind(kind).Should().Be(isLogged,
            "plain chat traffic is never logged, every addressed kind is");
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -5`
Expected: build error `NotificationHistoryItem` does not exist.

- [ ] **Step 3: Add the constants**

In `src/dotnet/Api/Constants.cs`, inside `public static class Notification`, right after the `ActiveReaderGrace` line:

```csharp
        public const int HistoryDefaultLimit = 64;
        public const int HistoryMaxLimit = 256;
        public static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(30);
```

- [ ] **Step 4: Add the two records**

Create `src/dotnet/Api/Notifications/NotificationHistoryItem.cs`:

```csharp
namespace ActualChat.Notifications;

/// <summary>
/// One row of a user's notification log: what a <see cref="Notification"/> looked like when it
/// fired, kept after the active set has dropped it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationHistoryItem(
    [property: DataMember(Order = 0), Key(0)] long Seq,
    [property: DataMember(Order = 1), Key(1)] NotificationKind Kind)
{
    [DataMember(Order = 2), Key(2)] public Moment SentAt { get; init; }
    [DataMember(Order = 3), Key(3)] public ChatId? ChatId { get; init; }
    [DataMember(Order = 4), Key(4)] public ChatEntryId? EntryId { get; init; }
    [DataMember(Order = 5), Key(5)] public AuthorId? AuthorId { get; init; }
    [DataMember(Order = 6), Key(6)] public string Title { get; init; } = "";
    [DataMember(Order = 7), Key(7)] public string Text { get; init; } = "";

    // Message (a row per incoming message in every subscribed chat) and SpeechStarted are
    // chat traffic, not something addressed to the user, so they never reach the log.
    public static bool IsLoggedKind(NotificationKind kind)
        => kind is NotificationKind.Mention
            or NotificationKind.Reply
            or NotificationKind.Reaction
            or NotificationKind.Attention
            or NotificationKind.Thread
            or NotificationKind.Invitation
            or NotificationKind.Conversation
            or NotificationKind.IncomingCall;
}
```

Create `src/dotnet/Api/Notifications/NotificationHistoryQuery.cs`:

```csharp
namespace ActualChat.Notifications;

/// <summary>
/// A page request over a user's notification log. <see cref="AfterSeq"/> is a cursor in walk
/// order: with <see cref="IsNewestFirst"/> the page continues to older rows, otherwise to newer.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationHistoryQuery
{
    // Empty = every logged kind
    [DataMember(Order = 0), Key(0)] public ApiArray<NotificationKind> Kinds { get; init; }
    // 0 = from the start of the walk
    [DataMember(Order = 1), Key(1)] public long AfterSeq { get; init; }
    // 0 = Constants.Notification.HistoryDefaultLimit; capped at HistoryMaxLimit
    [DataMember(Order = 2), Key(2)] public int Limit { get; init; }
    [DataMember(Order = 3), Key(3)] public bool IsNewestFirst { get; init; }
}
```

- [ ] **Step 5: Extend the API contracts**

In `src/dotnet/Api.Contracts/Notifications/INotifications.cs`, after `HasNotifiedMentionedMembers` and before the first `[CommandHandler]`:

```csharp
    // Not a compute method on purpose: every logged notification would otherwise invalidate every
    // cursor variant, and nothing reactive reads it - agents poll it with a cursor.
    Task<ApiArray<NotificationHistoryItem>> ListHistory(
        Session session, NotificationHistoryQuery query, CancellationToken cancellationToken);
```

In `src/dotnet/Notifications.Contracts/INotificationsBackend.cs`, after `GetUserNotificationInfo` and before `// Commands`:

```csharp
    // Not a compute method on purpose - see INotifications.ListHistory
    Task<ApiArray<NotificationHistoryItem>> ListHistory(
        UserId userId, NotificationHistoryQuery query, CancellationToken cancellationToken);
```

and in the `// Events` block, after `OnSignedOut`:

```csharp
    [EventHandler]
    Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken);
```

- [ ] **Step 6: Implement the service method and stub the backend**

In `src/dotnet/Notifications.Service/NotificationsService.cs`, after `HasNotifiedMentionedMembers`:

```csharp
    public virtual async Task<ApiArray<NotificationHistoryItem>> ListHistory(
        Session session, NotificationHistoryQuery query, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<NotificationHistoryItem>.Empty;

        return await Backend.ListHistory(account.Id, query, cancellationToken).ConfigureAwait(false);
    }
```

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`, after `GetUserNotificationInfo` (ends around line 142) add the read stub, and at the end of the event-handler section (after `OnSignedOut`) the event stub. Both are replaced in Task 3.

```csharp
    public virtual Task<ApiArray<NotificationHistoryItem>> ListHistory(
        UserId userId, NotificationHistoryQuery query, CancellationToken cancellationToken)
        => Task.FromResult(ApiArray<NotificationHistoryItem>.Empty);
```

```csharp
    // [EventHandler]
    public virtual Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;
```

- [ ] **Step 7: Build and run the test**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~NotificationHistoryItemTest" 2>&1 | tail -5`
Expected: build succeeds, 12 test cases pass.

Also build the two other consumers of the contracts so a missing member is caught here, not in Task 6:
Run: `dotnet build tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj 2>&1 | tail -3`
Expected: succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Notifications/NotificationHistoryItem.cs src/dotnet/Api/Notifications/NotificationHistoryQuery.cs src/dotnet/Api/Constants.cs src/dotnet/Api.Contracts/Notifications/INotifications.cs src/dotnet/Notifications.Contracts/INotificationsBackend.cs src/dotnet/Notifications.Service/NotificationsService.cs src/dotnet/Notifications.Service/NotificationsBackend.cs tests/Notifications.IntegrationTests/NotificationHistoryItemTest.cs
git commit -m "feat(notifications): history item, query and ListHistory contracts

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: History table and migration

**Files:**
- Create: `src/dotnet/Notifications.Service/Db/DbNotificationHistoryItem.cs`
- Modify: `src/dotnet/Notifications.Service/Db/NotificationDbContext.cs`
- Create (generated): `src/dotnet/Notifications.Service.Migration/Migrations/<timestamp>_Add_NotificationHistory.cs` + `.Designer.cs`, and the updated `NotificationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `NotificationHistoryItem`, `NotificationHistoryItem.IsLoggedKind` (Task 1).
- Produces: `DbNotificationHistoryItem` with `static string ComposeId(Notification)`, constructor `(Notification, long seq, Moment createdAt)`, `ToModel()`; `NotificationDbContext.NotificationHistory` DbSet.

- [ ] **Step 1: Add the entity**

Create `src/dotnet/Notifications.Service/Db/DbNotificationHistoryItem.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notifications.Db;

// Id is "{NotificationId}:{SentAt ticks}", so an at-least-once redelivery of the same
// UserNotifiedEvent maps to the same row and the DoNothing conflict strategy drops it.
[Table("NotificationHistory")]
[Index(nameof(UserId), nameof(Seq))]
[Index(nameof(UserId), nameof(Kind), nameof(Seq))]
[Index(nameof(CreatedAt))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbNotificationHistoryItem : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public long Seq { get; set; }
    public string UserId { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public string ChatId { get; set; } = "";
    public long EntryLid { get; set; }
    public string AuthorId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    public DateTime SentAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(Notification notification)
        => $"{notification.Id.Value}:{notification.SentAt.EpochOffset.Ticks}";

    public DbNotificationHistoryItem() { }
    public DbNotificationHistoryItem(Notification notification, long seq, Moment createdAt)
    {
        Id = ComposeId(notification);
        Seq = seq;
        UserId = notification.UserId.Value;
        Kind = notification.Kind;
        ChatId = (notification as ChatNotification)?.ChatId.Value ?? "";
        EntryLid = notification switch {
            ChatEntryNotification n => n.EntryLid,
            ChatEntryRelatedNotification n => n.EntryLid,
            _ => 0,
        };
        AuthorId = (notification as ChatNotification)?.AuthorId?.Value ?? "";
        Title = notification.Title;
        Text = notification.Text;
        SentAt = notification.SentAt.ToDateTime();
        CreatedAt = createdAt.ToDateTime();
    }

    public NotificationHistoryItem ToModel()
    {
        var chatId = ChatId.IsNullOrEmpty() ? null : ActualChat.ChatId.Parse(ChatId);
        return new NotificationHistoryItem(Seq, Kind) {
            SentAt = SentAt.ToMoment(),
            ChatId = chatId,
            EntryId = chatId is not null && EntryLid > 0 ? ChatEntryId.New(chatId, EntryLid) : null,
            AuthorId = AuthorId.IsNullOrEmpty() ? null : ActualChat.AuthorId.Parse(AuthorId),
            Title = Title,
            Text = Text,
        };
    }

    // Nested types

    internal class EntityConfiguration : IEntityTypeConfiguration<DbNotificationHistoryItem>
    {
        public void Configure(EntityTypeBuilder<DbNotificationHistoryItem> builder)
            => builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
    }
}
```

If `SuppressMessage` needs a using (`System.Diagnostics.CodeAnalysis`), check `DbUserNotifications.cs` in the same folder: it uses the attribute with only the usings shown above, so it is a global using.

- [ ] **Step 2: Register the entity in the DbContext**

In `src/dotnet/Notifications.Service/Db/NotificationDbContext.cs`, add the DbSet after `UserNotifications`:

```csharp
    public DbSet<DbNotificationHistoryItem> NotificationHistory { get; protected set; } = null!;
```

and in `OnModelCreating`, after the `userNotifications` collation line:

```csharp
        var notificationHistory = model.Entity<DbNotificationHistoryItem>();
        notificationHistory.Property(e => e.Id).UseCollation("C");
        notificationHistory.Property(e => e.UserId).UseCollation("C");
        notificationHistory.Property(e => e.ChatId).UseCollation("C");
        notificationHistory.Property(e => e.AuthorId).UseCollation("C");
```

- [ ] **Step 3: Build the migration project**

Run: `dotnet build src/dotnet/Notifications.Service.Migration/Notifications.Service.Migration.csproj 2>&1 | tail -3`
Expected: succeeds.

- [ ] **Step 4: Generate the migration**

The migration project ships its own `IDesignTimeDbContextFactory` (`NotificationDbContextContextFactory`), so no startup project or live DB is needed:

```bash
dotnet ef migrations add Add_NotificationHistory \
  --project src/dotnet/Notifications.Service.Migration/Notifications.Service.Migration.csproj \
  --startup-project src/dotnet/Notifications.Service.Migration/Notifications.Service.Migration.csproj \
  --no-build
```

Expected: three files change under `src/dotnet/Notifications.Service.Migration/Migrations/`: a new `<timestamp>_Add_NotificationHistory.cs`, its `.Designer.cs`, and `NotificationDbContextModelSnapshot.cs`.

- [ ] **Step 5: Inspect the generated `Up()`**

Open the new migration. It must create table `notification_history` with columns `id` (text, PK), `seq` (bigint), `user_id`, `kind` (integer), `chat_id`, `entry_lid` (bigint), `author_id`, `title`, `text`, `sent_at`, `created_at` (timestamp with time zone), collation `C` on `id`, `user_id`, `chat_id`, `author_id`, and three indexes: `ix_notification_history_user_id_seq`, `ix_notification_history_user_id_kind_seq`, `ix_notification_history_created_at`. The `.Designer.cs` header must carry `ProductVersion` `11.0.0-preview.6.26359.118`, the same as the previous Notifications migration. If anything else changed in the snapshot (an unrelated table drifting), stop and report it rather than committing the drift.

- [ ] **Step 6: Build the test project (the test host applies migrations on start)**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~NotificationDismissModeTest" 2>&1 | tail -5`
Expected: build succeeds and the existing dismiss-mode tests still pass, which proves the migration applies cleanly to the test database.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Notifications.Service/Db/DbNotificationHistoryItem.cs src/dotnet/Notifications.Service/Db/NotificationDbContext.cs src/dotnet/Notifications.Service.Migration/Migrations/
git commit -m "feat(notifications): NotificationHistory table and migration

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Write path and backend read

**Files:**
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs` (`OnNotify` around lines 144-200; the `ListHistory` and `OnUserNotifiedEvent` stubs from Task 1)
- Test: `tests/Notifications.IntegrationTests/NotificationHistoryTest.cs`

**Interfaces:**
- Consumes: `DbNotificationHistoryItem`, `NotificationDbContext.NotificationHistory` (Task 2); `NotificationHistoryQuery`, `NotificationHistoryItem.IsLoggedKind` (Task 1); `UserNotifiedEvent` (exists, `Backend/Events/UserNotifiedEvent.cs`).
- Produces: a working `INotificationsBackend.ListHistory` and `OnUserNotifiedEvent`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Notifications.IntegrationTests/NotificationHistoryTest.cs`:

```csharp
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationHistoryTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private INotificationsBackend Backend => Tester.NotificationsBackend;

    [Fact]
    public async Task MentionShouldBeLoggedAndSurviveRead()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(false, "Notification history chat");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        NotificationHistoryItem item = null!;
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            item = items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention).Subject;
        }, WaitTimeout);
        item.ChatId.Should().Be(chatId);
        item.EntryId.Should().Be(entry.Id);
        item.AuthorId.Should().Be(entry.AuthorId);
        item.Seq.Should().BePositive();

        await Commander.Call(new ChatPositionsBackend_Set(
            alice.Id, chatId, ChatPositionKind.Read, new ChatPosition(entry.LocalId)));
        await TestExt.When(async () => {
            var info = await Backend.GetUserNotificationInfo(alice.Id, CancellationToken.None);
            info.Items.Should().NotContain(n => n.Kind == NotificationKind.Mention,
                "reading the chat clears the active mention");
        }, WaitTimeout);
        var after = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        after.Should().ContainSingle(x => x.Kind == NotificationKind.Mention,
            "the log is not the active set: a read must not erase history");
    }

    [Fact]
    public async Task MessageKindShouldNotBeLogged()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var message = MessageNotification.New(alice.Id, chatId, 5, authorId) with {
            SentAt = now, Title = "Chat", Text = "plain traffic",
        };
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 6), authorId) with {
            SentAt = now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(message));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle(x => x.Kind == NotificationKind.Mention);
        }, WaitTimeout);
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().NotContain(x => x.Kind == NotificationKind.Message, "per-chat traffic is not addressed to anyone");
    }

    [Fact]
    public async Task RedeliveredEventShouldNotDuplicateTheRow()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var chatId = ChatId.Parse("the-actual-one");
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 7), AuthorId.New(chatId, 1)) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };

        // act
        await Queues.Enqueue(new UserNotifiedEvent(mention));
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // assert
        await TestExt.When(async () => {
            var items = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().ContainSingle();
        }, WaitTimeout);
        await Task.Delay(500);
        var all = await Backend.ListHistory(alice.Id, new NotificationHistoryQuery(), CancellationToken.None);
        all.Should().ContainSingle("the same notification with the same SentAt is one row however often it is delivered");
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~NotificationHistoryTest" 2>&1 | tail -15`
Expected: all three fail on the `ContainSingle` waits (the stubs return an empty array).

- [ ] **Step 3: Stamp `SentAt` before the event is enqueued**

In `NotificationsBackend.OnNotify`, the current order is: enqueue `UserNotifiedEvent`, read `info`, dormant check, stamp `SentAt`. Change it so the stamp comes first. Replace the block from `// A hook wants every notification` through the `if (IsSoftUpdate(...))` guard with:

```csharp
        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        if (notification.SentAt == default) {
            // Reuse an already-items notification's SentAt so a SentAt-less redelivery stays a
            // no-op (MergeWith treats an equal SentAt as a duplicate) instead of re-alerting; only a
            // genuinely first-seen notification is stamped Now. Stamped before the event below so
            // the history row's id (which embeds SentAt) is stable across redeliveries.
            var items = info.Items.FirstOrDefault(n => n.Id == notification.Id);
            notification = notification with { SentAt = items?.SentAt ?? Clocks.SystemClock.Now };
        }

        // A hook wants every notification, even one the recipient's own dormant/active-reader
        // filters would suppress, so this fires before those checks. Web hook fan-out is
        // best-effort relative to push, so a queue outage here must not cost the push below.
        try {
            await Queues.Enqueue(new UserNotifiedEvent(notification), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            Log.LogError(e, "UserNotifiedEvent enqueue failed. UserId={UserId}, NotificationId={NotificationId}",
                userId, notification.Id);
        }

        if (info.IsDormant) {
            DebugLog?.LogDebug("OnNotify: skipped (dormant). UserId={UserId}, NotificationId={NotificationId}",
                userId, notification.Id);
            return;
        }

        if (IsSoftUpdate(info, notification)) {
```

Keep everything after the `IsSoftUpdate` guard unchanged.

- [ ] **Step 4: Implement the event handler**

Replace the `OnUserNotifiedEvent` stub with:

```csharp
    // [EventHandler]
    public virtual async Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // The log is read by a plain method, nothing to invalidate

        var notification = eventCommand.Notification;
        if (!NotificationHistoryItem.IsLoggedKind(notification.Kind))
            return;

        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var item = new DbNotificationHistoryItem(notification, VersionGenerator.NextVersion(), Clocks.SystemClock.Now);
        dbContext.NotificationHistory.Add(item);
        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when (e.Entries.All(en => en.State == EntityState.Added)) {
            // A redelivered event maps to the same id: INSERT ... ON CONFLICT DO NOTHING affected 0 rows
        }
    }
```

Place it in the `// Events` group of handlers, after `OnSignedOut`, matching the interface order.

- [ ] **Step 5: Implement the backend read**

Replace the `ListHistory` stub with:

```csharp
    public virtual async Task<ApiArray<NotificationHistoryItem>> ListHistory(
        UserId userId, NotificationHistoryQuery query, CancellationToken cancellationToken)
    {
        var kinds = query.Kinds.Where(NotificationHistoryItem.IsLoggedKind).Distinct().ToArray();
        if (!query.Kinds.IsEmpty && kinds.Length == 0)
            return ApiArray<NotificationHistoryItem>.Empty;

        var limit = query.Limit <= 0
            ? Constants.Notification.HistoryDefaultLimit
            : Math.Min(query.Limit, Constants.Notification.HistoryMaxLimit);
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var sUserId = userId.Value;
        var afterSeq = query.AfterSeq;
        var items = dbContext.NotificationHistory.Where(x => x.UserId == sUserId);
        if (kinds.Length > 0)
            items = items.Where(x => kinds.Contains(x.Kind));
        if (query.IsNewestFirst) {
            if (afterSeq > 0)
                items = items.Where(x => x.Seq < afterSeq);
            items = items.OrderByDescending(x => x.Seq);
        }
        else
            items = items.Where(x => x.Seq > afterSeq).OrderBy(x => x.Seq);

        var dbItems = await items.Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        return dbItems.Select(x => x.ToModel()).ToApiArray();
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~NotificationHistoryTest" 2>&1 | tail -8`
Expected: 3 passed.

Then the whole notifications suite, to catch a regression from the `OnNotify` reorder:
Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build 2>&1 | tail -8`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Notifications.Service/NotificationsBackend.cs tests/Notifications.IntegrationTests/NotificationHistoryTest.cs
git commit -m "feat(notifications): log addressed notifications and read them by cursor

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Filter and cursor semantics through the API

**Files:**
- Test: `tests/Notifications.IntegrationTests/NotificationHistoryTest.cs` (add one test)

**Interfaces:**
- Consumes: `INotifications.ListHistory` (Task 1, implemented via Task 3).

This task adds no production code unless the test finds a defect; its value is pinning the paging contract before the MCP tool builds on it.

- [ ] **Step 1: Add the test**

Append to `NotificationHistoryTest`:

```csharp
    [Fact]
    public async Task ListHistoryShouldFilterByKindAndWalkByCursor()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var notifications = AppHost.Services.GetRequiredService<INotifications>();
        var chatId = ChatId.Parse("the-actual-one");
        var authorId = AuthorId.New(chatId, 1);
        var now = Clocks.SystemClock.Now;
        var expectedKinds = new List<NotificationKind>();
        for (var lid = 1; lid <= 5; lid++) {
            var entryId = ChatEntryId.New(chatId, lid);
            Notification n = lid % 2 == 0
                ? ReactionNotification.New(alice.Id, entryId, authorId)
                : MentionNotification.New(alice.Id, entryId, authorId);
            n = n with { SentAt = now + TimeSpan.FromMilliseconds(lid), Title = "Chat", Text = $"#{lid}" };
            expectedKinds.Add(n.Kind);
            await Queues.Enqueue(new UserNotifiedEvent(n));
        }
        await TestExt.When(async () => {
            var items = await notifications.ListHistory(Tester.Session, new NotificationHistoryQuery(), CancellationToken.None);
            items.Should().HaveCount(5);
        }, WaitTimeout);

        // act
        var all = await notifications.ListHistory(Tester.Session, new NotificationHistoryQuery(), CancellationToken.None);
        var reactions = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Kinds = ApiArray.New(NotificationKind.Reaction) }, CancellationToken.None);
        var unlogged = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Kinds = ApiArray.New(NotificationKind.Message) }, CancellationToken.None);
        var page1 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2 }, CancellationToken.None);
        var page2 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2, AfterSeq = page1[^1].Seq }, CancellationToken.None);
        var page3 = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { Limit = 2, AfterSeq = page2[^1].Seq }, CancellationToken.None);
        var newest = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { IsNewestFirst = true }, CancellationToken.None);
        var olderThanNewest = await notifications.ListHistory(Tester.Session,
            new NotificationHistoryQuery { IsNewestFirst = true, AfterSeq = newest[0].Seq }, CancellationToken.None);

        // assert
        all.Select(x => x.Text).Should().Equal(["#1", "#2", "#3", "#4", "#5"], "oldest first, in enqueue order");
        all.Select(x => x.Kind).Should().Equal(expectedKinds);
        all.Select(x => x.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        reactions.Should().HaveCount(2).And.OnlyContain(x => x.Kind == NotificationKind.Reaction);
        unlogged.Should().BeEmpty("Message is never logged, so filtering on it matches nothing");
        page1.Concat(page2).Concat(page3).Select(x => x.Seq).Should().Equal(all.Select(x => x.Seq),
            "cursor pages tile the log without overlap or gaps");
        page3.Should().ContainSingle();
        newest.Select(x => x.Seq).Should().Equal(all.Select(x => x.Seq).Reverse());
        olderThanNewest.Select(x => x.Seq).Should().Equal(newest.Skip(1).Select(x => x.Seq),
            "with newest-first the cursor continues to older rows");
    }
```

- [ ] **Step 2: Run it**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ListHistoryShouldFilterByKindAndWalkByCursor" 2>&1 | tail -8`
Expected: passes. If ordering fails because two rows share a `Seq`, `VersionGenerator.NextVersion()` is not monotonic on this host; report it, do not weaken the assertion.

- [ ] **Step 3: Commit**

```bash
git add tests/Notifications.IntegrationTests/NotificationHistoryTest.cs
git commit -m "test(notifications): history kind filter and cursor paging

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Retention pruner

**Files:**
- Create: `src/dotnet/Notifications.Service/NotificationHistoryPruner.cs`
- Modify: `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs`
- Test: `tests/Notifications.IntegrationTests/NotificationHistoryPrunerTest.cs`

**Interfaces:**
- Consumes: `NotificationDbContext.NotificationHistory`, `DbNotificationHistoryItem` (Task 2), `Constants.Notification.HistoryRetention` (Task 1).
- Produces: `NotificationHistoryPruner : WorkerBase` with `Task RunOnce(CancellationToken)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Notifications.IntegrationTests/NotificationHistoryPrunerTest.cs`:

```csharp
using ActualChat.Notifications.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationHistoryPrunerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private NotificationHistoryPruner Pruner => field ??= AppHost.Services.GetRequiredService<NotificationHistoryPruner>();

    [Fact]
    public async Task RunOnceShouldPruneOnlyRowsPastRetention()
    {
        // arrange
        var userId = UserId.New();
        var chatId = ChatId.Parse("the-actual-one");
        var dbHub = AppHost.Services.DbHub<NotificationDbContext>();
        var now = Clocks.SystemClock.Now;
        var oldId = await Insert(dbHub, userId, chatId, 1, now - TimeSpan.FromDays(31));
        var freshId = await Insert(dbHub, userId, chatId, 2, now - TimeSpan.FromMinutes(1));

        // act
        await Pruner.RunOnce(CancellationToken.None);

        // assert
        await using var dbContext = await dbHub.CreateDbContext();
        var remainingIds = await dbContext.NotificationHistory
            .Where(x => x.UserId == userId.Value)
            .Select(x => x.Id)
            .ToListAsync();
        remainingIds.Should().Equal([freshId], "only rows older than HistoryRetention are pruned");
        remainingIds.Should().NotContain(oldId);
    }

    // Private methods

    private static async Task<string> Insert(
        DbHub<NotificationDbContext> dbHub, UserId userId, ChatId chatId, long lid, Moment createdAt)
    {
        var notification = MentionNotification.New(userId, ChatEntryId.New(chatId, lid), AuthorId.New(chatId, 1))
            with { SentAt = createdAt, Title = "Chat", Text = "@you" };
        var item = new DbNotificationHistoryItem(notification, dbHub.VersionGenerator.NextVersion(), createdAt);
        await using var dbContext = await dbHub.CreateDbContext(readWrite: true);
        dbContext.Add(item);
        await dbContext.SaveChangesAsync();
        return item.Id;
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3`
Expected: build error, `NotificationHistoryPruner` does not exist.

- [ ] **Step 3: Add the pruner**

Create `src/dotnet/Notifications.Service/NotificationHistoryPruner.cs`:

```csharp
using ActualChat.Notifications.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notifications;

/// <summary>
/// Hourly sweep deleting notification history rows older than
/// <see cref="Constants.Notification.HistoryRetention"/>.
/// </summary>
public sealed class NotificationHistoryPruner : WorkerBase
{
    private static readonly RandomTimeSpan Period = TimeSpan.FromHours(1).ToRandom(0.25);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(5);
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(30, 600);

    private DbHub<NotificationDbContext> DbHub { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public NotificationHistoryPruner(IServiceProvider services)
    {
        DbHub = services.DbHub<NotificationDbContext>();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    public async Task RunOnce(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var cutoff = (Clocks.SystemClock.Now - Constants.Notification.HistoryRetention).ToDateTime();
        var prunedCount = await dbContext.NotificationHistory
            .Where(x => x.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (prunedCount > 0)
            Log.LogInformation("Pruned {Count} expired notification history rows", prunedCount);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        // PrependDelay wraps the cycling chain, so it staggers the host once rather than every cycle
        => AsyncChain.From(RunOnce)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelays, Log)
            .AppendDelay(Period, Clocks.CpuClock)
            .CycleForever()
            .PrependDelay(FirstDelay, Clocks.CpuClock)
            .Run(cancellationToken);
}
```

- [ ] **Step 4: Register it**

In `src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs`, after the `dbModule.AddDbContextServices<NotificationDbContext>(...)` call (the last statement of `InjectServices`):

```csharp
        services.AddSingleton<NotificationHistoryPruner>()
            .AddHostedService(c => c.GetRequiredService<NotificationHistoryPruner>());
```

- [ ] **Step 5: Run the test**

Run: `dotnet build tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~NotificationHistoryPrunerTest" 2>&1 | tail -8`
Expected: passes.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Notifications.Service/NotificationHistoryPruner.cs src/dotnet/Notifications.Service/Module/NotificationServiceModule.cs tests/Notifications.IntegrationTests/NotificationHistoryPrunerTest.cs
git commit -m "feat(notifications): prune history rows past 30 days

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: MCP tool `list_notifications`

**Files:**
- Create: `src/dotnet/Mcp/Models/McpNotification.cs`
- Create: `src/dotnet/Mcp/Tools/McpNotificationTools.cs`
- Modify: `src/dotnet/Mcp/Module/McpModule.cs`
- Modify: `tests/Mcp.IntegrationTests/McpToolSchemaTest.cs`
- Test: `tests/Mcp.IntegrationTests/McpNotificationToolsTest.cs`

**Interfaces:**
- Consumes: `INotifications.ListHistory`, `NotificationHistoryQuery`, `NotificationHistoryItem`, `NotificationHistoryItem.IsLoggedKind` (Task 1); `ChatEntry.ToMcpModel(...)` and `Moment.ToMcpMillis()` from `src/dotnet/Mcp/Models/McpModelExt.cs`; `McpSessionAccessor`.
- Produces: tool `list_notifications` returning `McpListNotificationsResult(McpNotification[] Items, long? NextAfterSeq)`.

- [ ] **Step 1: Write the failing tests**

Add `"list_notifications"` to `ExpectedTools` in `tests/Mcp.IntegrationTests/McpToolSchemaTest.cs`, as the last line of the array:

```csharp
        "search_messages", "search_contacts",
        "list_notifications",
```

Create `tests/Mcp.IntegrationTests/McpNotificationToolsTest.cs`:

```csharp
using ActualChat.Notifications;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Mcp.IntegrationTests;

[Collection(nameof(McpCollection))]
public class McpNotificationToolsTest(McpCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : McpTestBase<McpCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task MentionShouldBeListedWithItsMessage()
    {
        // arrange
        var alice = await Tester.SignInAsUniqueAlice();
        var aliceKey = await IssueApiKey("alice");
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(isPublicChat: false, title: "Notification history");
        var aliceAuthor = await Tester.InviteToChat(chatId, alice.Id);
        await using var client = await CreateClientWithRawKey(aliceKey);

        // act
        var entry = await Tester.CreateTextEntry(chatId, $"hi @a:{aliceAuthor.Id}");

        // assert
        McpListNotificationsResult mentions = null!;
        await TestExt.When(async () => {
            mentions = await CallTool<McpListNotificationsResult>(client, "list_notifications",
                new { kinds = new[] { "mention" } });
            mentions.Items.Should().ContainSingle();
        }, WaitTimeout);
        var item = mentions.Items.Single();
        item.Kind.Should().Be("mention");
        item.ChatId.Should().Be(chatId.Value);
        item.EntryId.Should().Be(entry.LocalId);
        item.AuthorId.Should().Be(entry.AuthorId.Value);
        item.Message.Should().NotBeNull("the anchored message is re-resolved through the caller's session");
        item.Message!.Text.Should().Contain("hi");
        mentions.NextAfterSeq.Should().Be(item.Seq);

        var reactions = await CallTool<McpListNotificationsResult>(client, "list_notifications",
            new { kinds = new[] { "reaction" } });
        reactions.Items.Should().BeEmpty("the kind filter excludes the mention");
        reactions.NextAfterSeq.Should().BeNull();

        var nothingNew = await CallTool<McpListNotificationsResult>(client, "list_notifications",
            new { afterSeq = mentions.NextAfterSeq });
        nothingNew.Items.Should().BeEmpty("the cursor skips what the caller has already seen");
    }

    [Fact]
    public async Task UnknownKindShouldBeAnError()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();

        // act
        var error = await CallToolExpectingError(client, "list_notifications", new { kinds = new[] { "bogus" } });

        // assert
        error.Should().Contain("bogus");
    }

    [Fact]
    public async Task MessageWithoutAccessShouldBeNull()
    {
        // arrange - a mention anchored at a chat the caller cannot read yields no message body
        var alice = await Tester.SignInAsUniqueAlice();
        var client = await CreateClient();
        var chatId = ChatId.Parse("the-actual-one");
        var mention = MentionNotification.New(alice.Id, ChatEntryId.New(chatId, 1), AuthorId.New(chatId, 1)) with {
            SentAt = Clocks.SystemClock.Now, Title = "Chat", Text = "@you",
        };
        await Queues.Enqueue(new UserNotifiedEvent(mention));

        // act & assert
        await TestExt.When(async () => {
            var result = await CallTool<McpListNotificationsResult>(client, "list_notifications", new { });
            var item = result.Items.Should().ContainSingle().Subject;
            item.Text.Should().Be("@you");
            item.Message.Should().BeNull();
        }, WaitTimeout);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet build tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj 2>&1 | tail -3`
Expected: build error, `McpListNotificationsResult` does not exist.

- [ ] **Step 3: Add the models**

Create `src/dotnet/Mcp/Models/McpNotification.cs`:

```csharp
using ActualChat.External;

namespace ActualChat.Mcp;

public sealed record McpNotification(
    long Seq,
    string Kind,
    long At,
    string? ChatId,
    long? EntryId,
    string? AuthorId,
    string Title,
    string Text,
    ExternalMessage? Message);

public sealed record McpListNotificationsResult(McpNotification[] Items, long? NextAfterSeq);
```

- [ ] **Step 4: Add the tool class**

Create `src/dotnet/Mcp/Tools/McpNotificationTools.cs`:

```csharp
using System.ComponentModel;
using ActualChat.External;
using ActualChat.Mcp.Auth;
using ActualChat.Notifications;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpNotificationTools(IServiceProvider services)
{
    private INotifications Notifications { get; } = services.GetRequiredService<INotifications>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "list_notifications", UseStructuredContent = true)]
    [Description("Lists the caller's notification history - mentions, replies, reactions, attention pings, " +
        "thread and conversation pings, invitations and incoming calls addressed to them - oldest first. " +
        "Plain chat traffic is not logged; rows older than 30 days are gone. " +
        "Persist `nextAfterSeq` and pass it back as `afterSeq` on the next call to see only what is new. " +
        "`limit` is capped at 256.")]
    public async Task<McpListNotificationsResult> ListNotifications(
        [Description("Kinds to include, e.g. [\"mention\", \"reaction\"]; empty = all. " +
            "Valid: mention, reply, reaction, attention, thread, invitation, conversation, incomingcall.")]
        string[]? kinds = null,
        [Description("Cursor: return items after this seq in walk order " +
            "(newer ones by default, older ones when `newestFirst` is set).")]
        long? afterSeq = null,
        [Description("Max items to return; capped at 256.")]
        int limit = Constants.Notification.HistoryDefaultLimit,
        [Description("Newest first instead of oldest first.")]
        bool newestFirst = false,
        CancellationToken cancellationToken = default)
    {
        var query = new NotificationHistoryQuery {
            Kinds = ParseKinds(kinds),
            AfterSeq = afterSeq ?? 0,
            Limit = limit,
            IsNewestFirst = newestFirst,
        };
        var items = await Notifications.ListHistory(Session, query, cancellationToken).ConfigureAwait(false);
        var result = new McpNotification[items.Count];
        for (var i = 0; i < items.Count; i++)
            result[i] = await ToMcpModel(items[i], cancellationToken).ConfigureAwait(false);
        return new McpListNotificationsResult(result, items.IsEmpty ? null : items[^1].Seq);
    }

    // Private methods

    private async Task<McpNotification> ToMcpModel(NotificationHistoryItem item, CancellationToken cancellationToken)
    {
        var message = item.EntryId is { } entryId
            ? await GetMessage(entryId, cancellationToken).ConfigureAwait(false)
            : null;
        return new McpNotification(
            item.Seq,
            item.Kind.ToString().ToLower(),
            item.SentAt.ToMcpMillis(),
            item.ChatId?.Value,
            item.EntryId?.LocalId,
            item.AuthorId?.Value,
            item.Title,
            item.Text,
            message);
    }

    private async Task<ExternalMessage?> GetMessage(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        // Resolved through the session: a chat the caller has since left yields no message body
        var chat = await Chats.Get(Session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var entry = await Chats.GetEntry(Session, entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var author = await Authors.Get(Session, entryId.ChatId, entry.AuthorId, cancellationToken).ConfigureAwait(false);
        var authorById = new Dictionary<AuthorId, Author?> { [entry.AuthorId] = author };
        return await entry.ToMcpModel(authorById, UrlMapper, MarkupParser, cancellationToken).ConfigureAwait(false);
    }

    private static ApiArray<NotificationKind> ParseKinds(string[]? kinds)
    {
        if (kinds is null || kinds.Length == 0)
            return ApiArray<NotificationKind>.Empty;

        var result = new List<NotificationKind>(kinds.Length);
        foreach (var name in kinds) {
            if (!Enum.TryParse<NotificationKind>(name, ignoreCase: true, out var kind)
                || !NotificationHistoryItem.IsLoggedKind(kind))
                throw new ArgumentOutOfRangeException(nameof(kinds), $"Unknown notification kind: '{name}'.");

            result.Add(kind);
        }
        return result.ToApiArray();
    }
}
```

If `Chats.Get` for a chat the caller cannot read throws instead of returning null, the `MessageWithoutAccessShouldBeNull` test fails with an exception in the tool result; in that case wrap only the `Chats.Get` call in `try { ... } catch (UnauthorizedException) { return null; }` and note it in the commit message.

- [ ] **Step 5: Register the tool type**

In `src/dotnet/Mcp/Module/McpModule.cs`, after `.WithTools<McpSearchTools>(serializerOptions)`:

```csharp
            .WithTools<McpNotificationTools>(serializerOptions);
```

(and drop the `;` from the previous line).

- [ ] **Step 6: Run the tests**

Run: `dotnet build tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj 2>&1 | tail -3 && dotnet test tests/Mcp.IntegrationTests/Mcp.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~McpNotificationToolsTest|FullyQualifiedName~McpToolSchemaTest" 2>&1 | tail -8`
Expected: 4 passed.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Mcp/Models/McpNotification.cs src/dotnet/Mcp/Tools/McpNotificationTools.cs src/dotnet/Mcp/Module/McpModule.cs tests/Mcp.IntegrationTests/McpToolSchemaTest.cs tests/Mcp.IntegrationTests/McpNotificationToolsTest.cs
git commit -m "feat(mcp): list_notifications tool over the notification history

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Documentation

**Files:**
- Create: `docs/integrations/notifications-api.md`
- Modify: `docs/index.md` (after the web hooks bullet, around line 98)
- Modify: `docs/notifications.md` (the server-side table and the Lifecycle paragraph)
- Modify: `docs/api-index.md` (the `INotifications` and `INotificationsBackend` lines)
- Modify: `docs/api-index-full.md` (the `## ActualChat.Api` section, next to `Notification` around line 565; the `## ActualChat.Notifications.Service` section around line 874)

**Interfaces:**
- Consumes: everything shipped in Tasks 1-6; the doc describes the wire shapes exactly as implemented.

- [ ] **Step 1: Write the integration doc**

Create `docs/integrations/notifications-api.md`:

````markdown
---
title: "Integrations: notification history"
description: The per-user notification log, its read API and the MCP tool, for agents that poll instead of receiving web hooks.
---

# Integrations: notification history

An agent that talks to Voxt through the MCP server needs to know when it was
addressed: mentioned, replied to, reacted to, pinged. The active notification
set (`INotifications.ListActive`) cannot answer that — it is the converged set
of banners on the user's devices, and a banner is gone the moment the chat is
read on any device. The notification history is the log behind it.

## What is logged

Every notification of an *addressed* kind, at the moment it fires:

| `kind` | Fires when |
|---|---|
| `mention` | Someone mentions you |
| `reply` | Someone replies to your message |
| `reaction` | Someone reacts to your message |
| `attention` | Someone pings you or the whole chat ("notify members") |
| `thread` | A thread is started on your message |
| `invitation` | You are added to a chat |
| `conversation` | A voice conversation in a chat you follow starts, gets a title, or ends |
| `incomingcall` | You are rung |

`message` (a row per incoming message in every chat you are in) and
`speechstarted` are chat traffic, not something addressed to you, and are never
logged. Muted chats do not notify, so they do not log either; a chat in
"important only" mode logs mentions but not replies.

A row is what the notification looked like when it fired: the kind, the chat,
the entry it anchors at, the author, and the title and text of the banner. It is
kept for **30 days** regardless of reads, dismissals or the chat being left, then
pruned.

## Read API

```csharp
Task<ApiArray<NotificationHistoryItem>> INotifications.ListHistory(
    Session session, NotificationHistoryQuery query, CancellationToken cancellationToken);
```

`NotificationHistoryQuery`:

| Field | Default | Meaning |
|---|---|---|
| `Kinds` | empty = all logged kinds | `ApiArray<NotificationKind>`; unlogged kinds never match |
| `AfterSeq` | 0 = start of the walk | Cursor: return rows after this `Seq` in walk order |
| `Limit` | 64 | Capped at 256 |
| `IsNewestFirst` | false | Walk from the newest row towards older ones |

`NotificationHistoryItem` carries `Seq` (the cursor), `Kind`, `SentAt`, `ChatId`,
`EntryId`, `AuthorId`, `Title` and `Text`. It is not a compute method: nothing
reactive depends on it, and every logged notification would otherwise
invalidate every cursor variant.

## MCP tool: `list_notifications`

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `kinds` | `string[]` | all | Any of the `kind` values above, case-insensitive; an unknown value is an error |
| `afterSeq` | `long` | none | Cursor, see below |
| `limit` | `int` | 64 | Capped at 256 |
| `newestFirst` | `bool` | false | Newest first |

Result:

```json
{
  "items": [{
    "seq": 1234, "kind": "mention", "at": 1758470400000,
    "chatId": "…", "entryId": 42, "authorId": "…",
    "title": "Dima mentioned you in Review Requests", "text": "…",
    "message": { /* ExternalMessage, or null */ }
  }],
  "nextAfterSeq": 1234
}
```

`message` is the anchored message in the same `ExternalMessage` shape
`list_messages` and the web hook `notification` event use, re-resolved through
the caller's session at read time — a chat the caller has since left yields
`null`, as does a removed entry.

**Polling.** Persist `nextAfterSeq` and pass it back as `afterSeq`: each call
then returns only what is new, and an empty `items` with `nextAfterSeq: null`
means nothing new. Walk oldest-first for this; `newestFirst` is for "what
happened lately", where `afterSeq` continues towards older rows.

Web hooks ([`web-hooks.md`](./web-hooks.md)) push the same events; the history
is for agents that would rather poll.
````

- [ ] **Step 2: Link it from the docs index**

In `docs/index.md`, after the web hooks bullet:

```markdown
- [Integrations: notification history](./integrations/notifications-api.md) — the
  per-user log of addressed notifications, `INotifications.ListHistory`, the
  `list_notifications` MCP tool, and cursor polling.
```

- [ ] **Step 3: Update the notifications architecture doc**

In `docs/notifications.md`, add a table row after the `Persistence` row:

```markdown
| History | `Notifications.Service/Db/DbNotificationHistoryItem.cs`, `NotificationHistoryPruner.cs` | Append-only per-user log of addressed notifications (`NotificationHistory` table), fed by `UserNotifiedEvent`, read by `INotifications.ListHistory` and the `list_notifications` MCP tool, pruned after 30 days. See [integrations/notifications-api.md](./integrations/notifications-api.md). |
```

and after the **Lifecycle** paragraph add:

```markdown
**History.** Removal is final for the active set but not for the record:
`OnNotify` enqueues `UserNotifiedEvent` for every notification before the
dormant and active-reader checks, and `NotificationsBackend.OnUserNotifiedEvent`
appends the addressed kinds (`NotificationHistoryItem.IsLoggedKind`) to
`NotificationHistory`. The row id embeds `SentAt`, which is why `OnNotify`
stamps `SentAt` before the event goes out. `ListActive` is not a history and
must not be read as one.
```

- [ ] **Step 4: Update the API indexes**

In `docs/api-index.md`, replace the `INotifications` line under *Other Services* with:

```markdown
- `INotifications` — push notifications; `ListHistory` reads the per-user log of addressed notifications by kind and cursor
```

and the `INotificationsBackend` line under *Backend Contracts* with:

```markdown
- `INotificationsBackend` — notification backend; also owns the `NotificationHistory` log (`ListHistory`, `OnUserNotifiedEvent`)
```

In `docs/api-index-full.md`, under `## ActualChat.Api` next to the `Notification` record line, add:

```markdown
- `NotificationHistoryItem` (record) - One row of a user's notification log; `IsLoggedKind` says which kinds are recorded.
- `NotificationHistoryQuery` (record) - Kind filter, cursor and limit for `INotifications.ListHistory`.
```

and under `## ActualChat.Notifications.Service`:

```markdown
- `NotificationHistoryPruner` - Hourly worker deleting notification history rows past the 30-day retention.
```

- [ ] **Step 5: Check the docs build if the site tooling is available**

Run: `ls docs/Build-Site.cmd && head -20 docs/Build-Site.cmd`
If it is a plain script the local environment can run, run it and fix any broken link it reports; otherwise skip and say so in the task report.

- [ ] **Step 6: Commit**

```bash
git add docs/integrations/notifications-api.md docs/index.md docs/notifications.md docs/api-index.md docs/api-index-full.md
git commit -m "docs(integrations): notification history API and MCP tool

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** Storage → Task 2. Write path incl. the `SentAt` reorder and the DoNothing dedup → Task 3. Retention → Task 5. Read API, defaults, clamping, unlogged-kind filter → Tasks 1, 3, 4. MCP tool, kind parsing, `message` via session, `nextAfterSeq` → Task 6. Access control → service resolves own account (Task 1), message re-resolved through session (Task 6, tested). Tests listed in the spec → Tasks 3, 4, 5, 6. Docs → Task 7. Out of scope items are not touched.

**Type consistency.** `NotificationHistoryItem(long Seq, NotificationKind Kind)` + `SentAt/ChatId?/EntryId?/AuthorId?/Title/Text` is used identically in Tasks 2, 3, 4, 6. `NotificationHistoryQuery { Kinds, AfterSeq, Limit, IsNewestFirst }` matches between Tasks 1, 3, 4, 6. `McpNotification` positional order (`Seq, Kind, At, ChatId, EntryId, AuthorId, Title, Text, Message`) matches the constructor call in Task 6 and the property names asserted in its tests. `DbNotificationHistoryItem(Notification, long seq, Moment createdAt)` matches Tasks 3 and 5.
