# Reaction Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface reaction notifications in the navbar notifications section — as a new entry-level "Reactions" tab, and as a third badge state on chat rows in the All tab.

**Architecture:** Reaction notifications already exist end-to-end on the server and reach the client through `INotifications.ListActive`; nothing renders them. Server-side, `ReactionNotification.MergeWith` starts accumulating reactors so one message collects one notification. Client-side, a new `NotificationsUI` compute service projects `ListActive` per kind and per chat, and the notifications panel reads it for the new tab, the widened All filter, the row badge and the navbar bell.

**Tech Stack:** C# 13 / .NET 9, ActualLab.Fusion compute services, Blazor (`ComputedStateComponent`), MessagePack + Newtonsoft + System.Text.Json serialization, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-08-30-reaction-notifications-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any C# or `.razor`.** This project deviates from stock .NET: no `Async` suffix, no XML docs on members, mixed brace style (Allman for classes/methods, K&R everywhere else), control-flow statements on their own line followed by a blank line.
- **Comments:** default to none. See CODING_STYLE → "Regular comments, docstrings, XML documentation comments". A comment is justified only for a non-obvious invariant, constraint or workaround.
- **Blazor components:** read `docs/ui/components.md` before creating any `.razor`. Root element gets a kebab-case class matching the component name; child classes use the `c-` prefix; a component with its own CSS gets a dedicated folder; CSS files must be `@import`ed from `src/dotnet/UI.Blazor.App/styles.css`.
- **Serialization:** every serializable member needs `[DataMember(Order = N), Key(N)]`. `[Key]` ordinals are wire format — append, never renumber or reuse. Never add MemoryPack attributes to new members.
- **Localization:** never hardcode user-visible English. Adding a key means editing all 19 hand-written catalogs plus a typed member, then regenerating the derived ones. `AppLocalizationTest` fails the build otherwise.
- **Build:** use `dotnet build ActualChat.CI.slnf`, not `ActualChat.sln` (the latter needs MAUI workloads).
- **Invariant globalization:** never pass `StringComparison.Ordinal` or `CultureInfo.InvariantCulture`; use `==`, plain `ToString()`, and `x.IsNullOrEmpty()`.
- **Cap value:** `Constants.Notification.MaxReactionAuthors = 5`.
- **Wire keys claimed by this change:** `ReactionNotification` key 9 (`AuthorIds`), key 10 (`Emojis`).

---

### Task 1: Accumulate reactors in `ReactionNotification.MergeWith`

**Files:**
- Modify: `src/dotnet/Api/Notifications/ReactionNotification.cs`
- Modify: `src/dotnet/Api/Constants.cs:412` (add next to `MaxTrackedAuthors`)
- Test: `tests/Notifications.IntegrationTests/ReactionNotificationMergeTest.cs` (create)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ReactionNotification.AuthorIds` (`ApiArray<AuthorId>`), `ReactionNotification.Emojis` (`ApiArray<Emoji>`), `Constants.Notification.MaxReactionAuthors` (`const int = 5`). Tasks 3, 5 and 7 read `AuthorIds` and `Emojis`.

**Background the implementer needs:** `MergeWith` is called server-side from `UserNotificationInfo.WithNotification`, reached at `src/dotnet/Notifications.Service/NotificationsBackend.cs:1742`. The line immediately below it does `if (ReferenceEquals(before, after))` and skips the push when the merge changed nothing — so returning the *existing instance* is how a no-op merge suppresses a duplicate notification. The queue is at-least-once, so redelivery of an identical event must hit that path.

- [ ] **Step 1: Write the failing tests**

Create `tests/Notifications.IntegrationTests/ReactionNotificationMergeTest.cs`:

```csharp
namespace ActualChat.Notifications.IntegrationTests;

public class ReactionNotificationMergeTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly ChatEntryId TestEntryId = ChatEntryId.New(TestChatId, 1);

    [Fact]
    public void MergeShouldAccumulateDistinctReactors()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var second = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2));

        // act
        var merged = (ReactionNotification)second.MergeWith(first);

        // assert
        merged.AuthorIds.Should().Equal([bob, kate]);
        merged.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
    }

    [Fact]
    public void MergeShouldKeepNewestDisplayFields()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1))
            with { Title = "Bob", Text = "reacted" };
        var second = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            with { Title = "Kate", Text = "reacted too" };

        // act
        var merged = (ReactionNotification)second.MergeWith(first);

        // assert
        merged.Title.Should().Be("Kate", because: "old clients read Title and must see the latest reaction");
        merged.AuthorId.Should().Be(kate);
    }

    [Fact]
    public void MergeShouldNotLetOlderEventRegressDisplayFields()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var newer = NewReaction(kate, Emojis.Party, Moment.EpochStart + TimeSpan.FromSeconds(2))
            with { Title = "Kate" };
        var older = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1))
            with { Title = "Bob" };

        // act
        var merged = (ReactionNotification)older.MergeWith(newer);

        // assert
        merged.Title.Should().Be("Kate");
        merged.SentAt.Should().Be(newer.SentAt);
        merged.AuthorIds.Should().Contain(bob);
    }

    [Fact]
    public void MergeShouldReturnExistingInstanceOnRedelivery()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var first = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var merged = first.MergeWith(null);
        var redelivered = NewReaction(bob, Emojis.Awesome, Moment.EpochStart + TimeSpan.FromSeconds(2));

        // act
        var result = redelivered.MergeWith(merged);

        // assert
        result.Should().BeSameAs(merged, because: "an unchanged merge must suppress the duplicate push");
    }

    [Fact]
    public void MergeShouldFreezeAtAuthorCap()
    {
        // arrange
        Notification accumulated = NewReaction(AuthorId.New(TestChatId, 100), Emojis.Awesome, Moment.EpochStart);
        for (var i = 1; i <= Constants.Notification.MaxReactionAuthors; i++) {
            var next = NewReaction(
                AuthorId.New(TestChatId, 100 + i),
                Emojis.Party,
                Moment.EpochStart + TimeSpan.FromSeconds(i));
            accumulated = next.MergeWith(accumulated);
        }
        var atCap = (ReactionNotification)accumulated;

        // act
        var extra = NewReaction(
            AuthorId.New(TestChatId, 999),
            Emojis.Awesome,
            Moment.EpochStart + TimeSpan.FromMinutes(1));
        var result = extra.MergeWith(atCap);

        // assert
        atCap.AuthorIds.Count.Should().Be(Constants.Notification.MaxReactionAuthors);
        result.Should().BeSameAs(atCap, because: "past the cap the notification stops changing and stops re-pushing");
    }

    private static ReactionNotification NewReaction(AuthorId authorId, Emoji emoji, Moment sentAt)
        => ReactionNotification.New(TestUserId, TestEntryId, authorId) with {
            AuthorIds = ApiArray.New(authorId),
            Emojis = ApiArray.New(emoji),
            SentAt = sentAt,
        };
}
```

`Emojis.Awesome` (🤩) and `Emojis.Party` (🥳) are real members of `src/dotnet/Api/Identifiers/Emojis.cs` — verified. That file has no `ThumbsUp` or `Tada`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ReactionNotificationMergeTest"
```

Expected: compile error — `AuthorIds`, `Emojis` and `MaxReactionAuthors` do not exist.

- [ ] **Step 3: Add the cap constant**

In `src/dotnet/Api/Constants.cs`, directly below `public const int MaxTrackedAuthors = 8;`:

```csharp
        // Past this many distinct reactors a reaction notification stops changing - and so stops
        // re-pushing - because further reactors would only move a number the row already shows.
        public const int MaxReactionAuthors = 5;
```

- [ ] **Step 4: Add the members and the merge override**

Replace the body of `src/dotnet/Api/Notifications/ReactionNotification.cs` with:

```csharp
namespace ActualChat.Notifications;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ReactionNotification(NotificationId Id, long Version = 0)
    : ChatEntryNotification(Id, Version)
{
    // The anchor entry is the recipient's own message, which their Read position already covers -
    // OnRead would drop this before it ever reached a device. The chat view clears it instead,
    // once the entry is actually on screen.
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override NotificationDismissMode DismissMode => NotificationDismissMode.OnView;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override Moment? ExpiresAt => SentAt + Constants.Notification.ReactionLifespan;

    // Keys 9 and 10 are free within this subtype - union members serialize independently, and
    // CallNotification / ConversationNotification already reuse the same range.
    [DataMember(Order = 9), Key(9)]
    public ApiArray<AuthorId> AuthorIds { get; init; }
    [DataMember(Order = 10), Key(10)]
    public ApiArray<Emoji> Emojis { get; init; }

    public static ReactionNotification New(UserId userId, ChatEntryId entryId, AuthorId? authorId = null)
        => new(NotificationId.New(userId, NotificationKind.Reaction, entryId.Value)) {
            AuthorId = authorId,
            AuthorIds = authorId is { } id ? ApiArray.New(id) : default,
        };

    public override Notification MergeWith(Notification? existing)
    {
        if (existing is not ReactionNotification e)
            return base.MergeWith(existing);

        // Title/Text/IconUrl/AuthorId are all old clients read, so they must keep describing the
        // latest reaction: freezing at the cap would otherwise strand them on a stale reactor.
        if (e.AuthorIds.Count >= Constants.Notification.MaxReactionAuthors)
            return e;

        var authorIds = e.AuthorIds;
        foreach (var authorId in AuthorIds)
            if (!authorIds.Contains(authorId))
                authorIds = authorIds.With(authorId);
        var emojis = e.Emojis;
        foreach (var emoji in Emojis)
            if (!emojis.Contains(emoji))
                emojis = emojis.With(emoji);
        if (authorIds.Count == e.AuthorIds.Count && emojis.Count == e.Emojis.Count)
            return e;

        // Newest-of-the-two rather than always the incoming: an out-of-order older event must not
        // regress the fields old clients render.
        var newest = SentAt > e.SentAt ? this : e;
        return newest with {
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            SentAt = Moment.Max(e.SentAt, SentAt),
            AuthorIds = authorIds,
            Emojis = emojis,
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~ReactionNotificationMergeTest"
```

Expected: 5 passed.

- [ ] **Step 6: Populate `Emojis` at the send site**

`ReactionNotification.New` cannot see the emoji, so `NotificationsBackend` must attach it. In `src/dotnet/Notifications.Service/NotificationsBackend.cs`, `OnReactionChangedEvent` ends with a call to `EnqueueMessageRelatedNotifications`. The notification is constructed inside that helper's `kind switch` at line ~1375. Thread the emoji through: add a `Emoji? reactionEmoji = null` optional parameter to the two public `EnqueueMessageRelatedNotifications` overloads and the private one, pass `reaction.Emoji` from `OnReactionChangedEvent`, and in the switch write:

```csharp
                NotificationKind.Reaction => ReactionNotification.New(otherUserId, fullEntryId, changeAuthor.Id) with {
                    Emojis = reactionEmoji is { } emoji ? ApiArray.New(emoji) : default,
                },
```

- [ ] **Step 7: Build and run the whole notifications suite**

```bash
dotnet build ActualChat.CI.slnf
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj
```

Expected: build succeeds, all tests pass. `NotificationModeTest` and `NotificationDismissModeTest` exercise the reaction path and must stay green.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Notifications/ReactionNotification.cs src/dotnet/Api/Constants.cs \
  src/dotnet/Notifications.Service/NotificationsBackend.cs \
  tests/Notifications.IntegrationTests/ReactionNotificationMergeTest.cs
git commit -m "feat(notifications): accumulate reactors on a reaction notification"
```

---

### Task 2: Wire-compatibility tests for the new keys

**Files:**
- Modify: `tests/Notifications.IntegrationTests/NotificationSerializationTests.cs`

**Interfaces:**
- Consumes: `ReactionNotification.AuthorIds`, `ReactionNotification.Emojis` from Task 1.
- Produces: nothing.

**Why this is its own task:** Task 1's merge logic is correct in memory even if the two new members never survive a round-trip. Key 9/10 reuse inside a union subtype is the risky claim in the spec and deserves its own gate.

- [ ] **Step 1: Write the failing tests**

Append to `NotificationSerializationTests`:

```csharp
    [Fact]
    public void AccumulatedReactorsShouldSurviveRoundtrip()
    {
        // arrange
        var bob = AuthorId.New(TestChatId, 5);
        var kate = AuthorId.New(TestChatId, 6);
        var entryId = ChatEntryId.New(TestChatId, 1);
        var notification = ReactionNotification.New(TestUserId, entryId, kate) with {
            Version = 1,
            Title = "Kate",
            Text = "reacted to your message",
            AuthorIds = ApiArray.New(bob, kate),
            Emojis = ApiArray.New(Emojis.Awesome, Emojis.Party),
        };

        // act
        var deserialized = AssertMessagePackRoundtrip(notification);

        // assert
        deserialized.AuthorIds.Should().Equal([bob, kate]);
        deserialized.Emojis.Should().Equal([Emojis.Awesome, Emojis.Party]);
    }

    [Fact]
    public void ReactionWithoutAccumulatedReactorsShouldRoundtrip()
    {
        // arrange
        // The shape a pre-accumulation blob has: keys 9 and 10 absent.
        var entryId = ChatEntryId.New(TestChatId, 1);
        var notification = new ReactionNotification(
            NotificationId.New(TestUserId, NotificationKind.Reaction, entryId.Value)) with {
            Version = 1,
            Title = "Kate",
            Text = "reacted to your message",
        };

        // act
        var deserialized = AssertMessagePackRoundtrip(notification);

        // assert
        deserialized.AuthorIds.Should().BeEmpty();
        deserialized.Emojis.Should().BeEmpty();
        deserialized.Title.Should().Be("Kate");
    }
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj \
  --filter "FullyQualifiedName~NotificationSerializationTests"
```

Expected: PASS. If `AccumulatedReactorsShouldSurviveRoundtrip` fails on the emoji array, `Emoji` needs an `ApiArray` formatter keep — see Step 3.

- [ ] **Step 3: Regenerate the AOT type registrations**

`ApiArray<Emoji>` is a newly reachable serializable shape, so the generated keeps must be refreshed:

```bash
./run-aot-type-generator.cmd
git diff --stat src/dotnet/*/Module/*AotSource.g.cs
```

Expected: `ApiAotSource.g.cs` gains `ApiArrayMessagePackFormatter<Emoji>`-related keeps. If the diff is empty, the shape was already covered — that is fine, move on.

- [ ] **Step 4: Commit**

```bash
git add tests/Notifications.IntegrationTests/NotificationSerializationTests.cs src/dotnet
git commit -m "test(notifications): cover reaction accumulation wire format"
```

---

### Task 3: `NotificationsUI` client projection service

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/NotificationsUI.cs`
- Modify: `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs:64` (register beside `NotificationsPanelUI`)
- Modify: `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs:54` (expose beside `NotificationsPanelUI`)

**Interfaces:**
- Consumes: `ReactionNotification.AuthorIds` / `Emojis` from Task 1; `Hub.Notifications.ListActive(Session, ct)`.
- Produces:
  - `NotificationsUI.ListByKind(NotificationKind kind, CancellationToken ct) -> Task<ApiArray<Notification>>` — `SentAt` descending.
  - `NotificationsUI.GetReactionState(ChatId chatId, CancellationToken ct) -> Task<ChatReactionState>` where `public readonly record struct ChatReactionState(Emoji? Emoji, Moment SentAt)` — `default` when the chat has no reaction notification.
  - `NotificationsUI.ListReactedChatIds(CancellationToken ct) -> Task<ApiArray<ChatId>>`.
  - Hub accessor `AppUIHub.NotificationsUI`.
  Tasks 4, 5, 6 and 7 all call these.

**Note on placement:** CODING_STYLE rule 14 says extend an existing UI service rather than adding one. The spec records why this is an exception — `NotificationsPanelUI` owns a grace-period timer over the *chat* list and holds mutable per-filter state, a different lifetime and a different source. Do not fold this into it.

- [ ] **Step 1: Write the failing test**

Create `tests/Chat.UI.Blazor.UnitTests/NotificationsUIProjectionTest.cs`. That project is the one that references `UI.Blazor.App`; `tests/UI.Blazor.UnitTests` references only `UI.Blazor` and cannot see `NotificationsUI`. The projections are pure functions over an `ApiArray<Notification>`, so test them through internal statics rather than standing up a hub:

```csharp
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class NotificationsUIProjectionTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly UserId TestUserId = UserId.New();
    private static readonly ChatId ChatA = ChatId.Parse("the-actual-one");

    [Fact]
    public void ListByKindShouldFilterAndSortNewestFirst()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1));
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2));
        var mention = MentionNotification.New(TestUserId, ChatEntryId.New(ChatA, 3));
        var active = ApiArray.New<Notification>(older, mention, newer);

        // act
        var result = NotificationsUI.SelectByKind(active, NotificationKind.Reaction);

        // assert
        result.Select(x => x.Id).Should().Equal([newer.Id, older.Id]);
    }

    [Fact]
    public void ReactionStateShouldTakeNewestReactionForChat()
    {
        // arrange
        var older = NewReaction(1, Moment.EpochStart + TimeSpan.FromSeconds(1)) with {
            Emojis = ApiArray.New(Emojis.Awesome),
        };
        var newer = NewReaction(2, Moment.EpochStart + TimeSpan.FromSeconds(2)) with {
            Emojis = ApiArray.New(Emojis.Party),
        };
        var active = ApiArray.New<Notification>(older, newer);

        // act
        var state = NotificationsUI.SelectReactionState(active, ChatA);

        // assert
        state.Emoji.Should().Be(Emojis.Party);
        state.SentAt.Should().Be(newer.SentAt);
    }

    [Fact]
    public void ReactionStateShouldBeDefaultForChatWithoutReactions()
    {
        // act
        var state = NotificationsUI.SelectReactionState(ApiArray<Notification>.Empty, ChatA);

        // assert
        state.Should().Be(default(ChatReactionState));
    }

    private static ReactionNotification NewReaction(long entryLid, Moment sentAt)
        => ReactionNotification.New(TestUserId, ChatEntryId.New(ChatA, entryLid)) with { SentAt = sentAt };
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~NotificationsUIProjectionTest"
```

Expected: compile error — `NotificationsUI` does not exist.

- [ ] **Step 3: Write the service**

Create `src/dotnet/UI.Blazor.App/Services/NotificationsUI.cs`:

```csharp
using ActualChat.Notifications;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-kind and per-chat projections of the user's active notification set, so the
/// notifications panel, its badges and the navbar bell share one computed over
/// <see cref="INotifications.ListActive"/>.
/// </summary>
public class NotificationsUI(AppUIHub hub) : IComputeService
{
    private AppUIHub Hub { get; } = hub;
    private INotifications Notifications => field ??= Hub.Notifications;
    private Session Session => Hub.Session;

    [ComputeMethod]
    public virtual async Task<ApiArray<Notification>> ListByKind(
        NotificationKind kind, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return SelectByKind(active, kind);
    }

    [ComputeMethod]
    public virtual async Task<ChatReactionState> GetReactionState(
        ChatId chatId, CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return SelectReactionState(active, chatId);
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<ChatId>> ListReactedChatIds(CancellationToken cancellationToken = default)
    {
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return active
            .OfType<ReactionNotification>()
            .Select(x => x.ChatId)
            .Distinct()
            .ToApiArray();
    }

    // Internal methods

    // Internal rather than private so the projections can be tested without a hub.
    internal static ApiArray<Notification> SelectByKind(ApiArray<Notification> active, NotificationKind kind)
        => active
            .Where(x => x.Kind == kind)
            .OrderByDescending(x => x.SentAt)
            .ToApiArray();

    internal static ChatReactionState SelectReactionState(ApiArray<Notification> active, ChatId chatId)
    {
        var newest = active
            .OfType<ReactionNotification>()
            .Where(x => x.ChatId == chatId)
            .MaxBy(x => x.SentAt);
        if (newest is null)
            return default;

        return new ChatReactionState(newest.Emojis.LastOrDefault(), newest.SentAt);
    }
}

public readonly record struct ChatReactionState(Emoji? Emoji, Moment SentAt);
```

- [ ] **Step 4: Register the service and expose it on the hub**

In `src/dotnet/UI.Blazor.App/Module/BlazorUIAppModule.cs`, directly after line 64:

```csharp
        fusion.AddService<NotificationsUI>(ServiceLifetime.Scoped);
```

In `src/dotnet/UI.Blazor.App/Services/AppUIHub.cs`, directly after line 54:

```csharp
    public NotificationsUI NotificationsUI => field ??= Services.GetRequiredService<NotificationsUI>();
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet build ActualChat.CI.slnf
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~NotificationsUIProjectionTest"
```

Expected: 3 passed.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/UI.Blazor.App tests/Chat.UI.Blazor.UnitTests
git commit -m "feat(ui): add NotificationsUI projections over the active notification set"
```

---

### Task 4: Reaction badge on the chat row

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatInfo.cs` (the `ChatUnreadState` struct at the bottom)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.cs:286-304` (`GetUnreadState`)
- Modify: `src/dotnet/UI.Blazor.App/Components/UnreadCount.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatList/ChatListItem.razor:247-266, 356-357`

**Interfaces:**
- Consumes: `NotificationsUI.GetReactionState` and `ChatReactionState` from Task 3.
- Produces: `ChatUnreadState.ReactionEmoji` (`Emoji?`); `UnreadCount.ReactionEmoji` parameter (`Emoji?`).

**Design note the implementer must respect:** reaction state deliberately does **not** go on `ChatInfo`. `ChatUI.GetPreview` carries a comment at `ChatUI.cs:160` explaining why per-row state is kept out of `Get()` — anything in `ChatInfo` invalidates the whole chat list when it changes. `GetUnreadState` is the existing consolidated per-row channel and is where this belongs.

- [ ] **Step 1: Extend `ChatUnreadState`**

At the bottom of `src/dotnet/UI.Blazor.App/Services/ChatInfo.cs`:

```csharp
public readonly record struct ChatUnreadState(
    Trimmed<int> Count,
    bool HasOwnMention,
    bool HasUnmutedUnread,
    Emoji? ReactionEmoji = null);
```

- [ ] **Step 2: Feed it from `GetUnreadState`**

In `ChatUI.GetUnreadState`, replace the final `return` with:

```csharp
        var reactionState = await NotificationsUI.GetReactionState(chatId, cancellationToken).ConfigureAwait(false);
        return isReadingTail
            ? default
            : new ChatUnreadState(
                chatInfo.UnreadCount,
                chatInfo.HasUnreadOwnMention,
                chatInfo.UnmutedUnreadCount > 0 && chatInfo.UnreadCount > 0,
                reactionState.Emoji);
```

Add the DI accessor alongside the other lazy hub properties in `ChatUI`:

```csharp
    private NotificationsUI NotificationsUI => field ??= Hub.NotificationsUI;
```

- [ ] **Step 3: Add the third badge state**

In `src/dotnet/UI.Blazor.App/Components/UnreadCount.razor`, replace the opening `@{ }` block and add the parameter:

```razor
@{
    _lastRenderTime = Clock.Now;
    if (Value == 0 && !HasMentions && ReactionEmoji is null)
        return;

    // Strict precedence: a mention outranks a count, which outranks a reaction.
    var text = HasMentions ? "@"
        : Value != 0 ? Value.FormatK()
        : "";
    var emoji = HasMentions || Value != 0 ? null : ReactionEmoji;
    var isMuted = NotificationMode switch {
        ChatNotificationMode.ImportantOnly => !HasMentions,
        ChatNotificationMode.Muted => !HasMentions,
        _ => false,
        };
    var bgColor = isMuted ? "bg-counter" : "bg-primary";
    var cursorClass = Click.HasDelegate ? "cursor-pointer" : "";
    var mentionClass = HasMentions ? "unread-mention" : "";
    var reactionClass = emoji is not null ? "unread-reaction" : "";
    var cssClass = $"message-counter-badge {cursorClass} {mentionClass} {reactionClass}";
}

<Badge Class="@cssClass" Color="@bgColor" Click="@Click">
    @if (emoji is not null) {
        <EmojiIcon Emoji="@emoji"/>
    }
    else {
        @text
    }
</Badge>
```

Add next to the other parameters:

```csharp
    [Parameter] public Emoji? ReactionEmoji { get; set; }
```

Open `src/dotnet/UI.Blazor.App/Components/Reactions/EmojiIcon.razor` first and match its actual parameter name and type — if it takes something other than an `Emoji`, adapt the call rather than changing `EmojiIcon`.

- [ ] **Step 4: Style the new badge state**

`unread-reaction` must not inherit the counter's background. Find the file that defines `.message-counter-badge` (`grep -rn "message-counter-badge" src/dotnet/UI.Blazor.App --include=*.css`) and add beside it:

```css
.message-counter-badge.unread-reaction {
    @apply bg-transparent;
    @apply p-0;
}
```

- [ ] **Step 5: Pass it through `ChatListItem`**

In `ChatListItem.razor`, add to the model record (near `HasUnreadOwnMention` at line 357):

```csharp
        public Emoji? ReactionEmoji { get; init; }
```

Set it in the returned model (near line 255):

```csharp
            ReactionEmoji = unreadState.ReactionEmoji,
```

And pass it at all three `<UnreadCount .../>` call sites (lines ~78, ~109, ~121):

```razor
                                ReactionEmoji="@m.ReactionEmoji"
```

- [ ] **Step 6: Build**

```bash
dotnet build ActualChat.CI.slnf
```

Expected: succeeds. `UnreadCount` is used outside `ChatListItem` too — `grep -rn "<UnreadCount" src/dotnet --include=*.razor` — but the new parameter is optional, so those sites keep compiling and keep their current behaviour.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App
git commit -m "feat(ui): show a reaction emoji badge on chat rows"
```

---

### Task 5: Reaction-only chats in the All tab, and the navbar bell

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatListUI.cs:203-225` (`ListUnorderedForDisplay`)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatList/NotificationsNavbarWidget.razor` (`HasTab`)
- Modify: `src/dotnet/UI.Blazor.App/Components/LeftPanel/LeftPanelButtons.razor:60-76` (`ComputeState`, `Model`)

**Interfaces:**
- Consumes: `NotificationsUI.ListReactedChatIds` from Task 3.
- Produces: nothing new; changes the membership of the existing lists.

**Design note:** `ChatListFilter.ChatInfoFilter` takes only a `ChatInfo`, and reaction state deliberately is not on `ChatInfo` (Task 4). So membership is widened where the panel *already* injects extra chats: `ListUnorderedForDisplay` unions `NotificationsPanelUI.GetExpiring` for the same reason. Follow that pattern — do not add a field to `ChatInfo`, and do not add a new `ChatListFilter`.

- [ ] **Step 1: Union reacted chats into the panel list**

In `ChatListUI.ListUnorderedForDisplay`, after the existing `expiring` union and before `return result;`, restructure so both unions apply:

```csharp
        var filter = settings.GetFilter();
        var chatById = await ListUnordered(placeId, filter, cancellationToken).ConfigureAwait(false);
        if (!filter.AcrossPlace)
            return chatById;

        var expiring = await NotificationsPanelUI.GetExpiring(filter.Id, cancellationToken).ConfigureAwait(false);
        var reactedChatIds = filter == ChatListFilter.UnreadMentions
            ? ApiArray<ChatId>.Empty
            : await NotificationsUI.ListReactedChatIds(cancellationToken).ConfigureAwait(false);
        if (expiring.Count == 0 && reactedChatIds.Count == 0)
            return chatById;

        // The ChatInfo each one had on its way out, so this needs no lookup over every chat.
        var result = new Dictionary<ChatId, ChatInfo>(chatById);
        foreach (var (chatId, chatInfo) in expiring)
            result.TryAdd(chatId, chatInfo);
        foreach (var chatId in reactedChatIds) {
            if (result.ContainsKey(chatId))
                continue;

            var chatInfo = await ChatUI.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chatInfo is not null && filter.Invoke(chatInfo) is false)
                result.TryAdd(chatId, chatInfo);
        }
        return result;
```

Note the `filter.Invoke(chatInfo) is false` guard: a reacted chat that *already* satisfies the filter came through `ListUnordered` and is in `chatById`; this branch only adds the ones the filter rejected. The Mentions tab is excluded outright — it means own-mentions and nothing else.

Add the DI accessor beside the other lazy hub properties in `ChatListUI`:

```csharp
    private NotificationsUI NotificationsUI => field ??= Hub.NotificationsUI;
```

- [ ] **Step 2: Keep the All / People tabs visible for a reaction-only state**

In `NotificationsNavbarWidget.razor`, extend `HasTab`:

```csharp
    private async Task<bool> HasTab(ChatListFilter filter, CancellationToken cancellationToken) {
        // A tab stays visible while it still holds recently-read chats, not only while there are unread ones.
        var expiring = await NotificationsPanelUI.GetExpiring(filter.Id, cancellationToken).ConfigureAwait(false);
        if (expiring.Count > 0)
            return true;

        var count = await ChatListUI.GetUnreadChatCount(null, filter, cancellationToken).ConfigureAwait(false);
        if (count > 0)
            return true;
        if (filter == ChatListFilter.UnreadMentions)
            return false;

        var reactedChatIds = await NotificationsUI.ListReactedChatIds(cancellationToken).ConfigureAwait(false);
        return reactedChatIds.Count > 0;
    }
```

Add the accessor next to the existing ones in the same `@code` block:

```csharp
    private NotificationsUI NotificationsUI => Hub.NotificationsUI;
```

- [ ] **Step 3: Light the navbar bell for a reaction-only state**

In `LeftPanelButtons.razor`, extend the model and `ComputeState`:

```csharp
    public sealed record Model(
        bool IsActive = false,
        bool HasUnread = false,
        bool HasRetained = false,
        bool HasReactions = false);
```

```csharp
        var chatById = await ChatListUI
            .ListUnordered(null, ChatListFilter.Unread, cancellationToken)
            .ConfigureAwait(true);
        var expiring = await NotificationsPanelUI
            .GetExpiring(ChatListFilter.Unread.Id, cancellationToken)
            .ConfigureAwait(true);
        var reactedChatIds = await Hub.NotificationsUI
            .ListReactedChatIds(cancellationToken)
            .ConfigureAwait(true);
        return new(true, chatById.Count > 0, expiring.Count > 0, reactedChatIds.Count > 0);
```

Then update the two places that gate on the model. The button's visibility condition (line ~27):

```razor
        @if (m.IsActive && (m.HasUnread || m.HasRetained || m.HasReactions)) {
```

and the red dot:

```razor
                    @if (m.HasUnread || m.HasReactions) {
                        <Badge Class="c-unread-dot" Color="bg-danger"/>
                    }
```

Finally, `OnAfterRender` falls back to the Chats group when nothing is left; include reactions so the panel does not close under a reaction:

```csharp
        if (_hadUnread && !m.HasUnread && !m.HasRetained && !m.HasReactions
            && NavbarUI.SelectedGroupId == NavbarGroupIds.Unread)
            NavbarUI.SelectGroup(NavbarGroupIds.Chats, false);
```

- [ ] **Step 4: Build**

```bash
dotnet build ActualChat.CI.slnf
```

Expected: succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App
git commit -m "feat(ui): surface reaction-only chats in the notifications panel"
```

---

### Task 6: Sort reacted chats to the top of the panel

**Files:**
- Modify: `src/dotnet/Api/Chat/ChatListPreOrder.cs`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatListExt.cs:40-65`
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatListUI.cs:124-133` (`List`)
- Test: `tests/Chat.UI.Blazor.UnitTests/ChatListOrderTest.cs` (create)

**Interfaces:**
- Consumes: `NotificationsUI.GetReactionState` from Task 3.
- Produces: `ChatListPreOrder.ReactionsFirst`; `ChatListExt.OrderBy(this IEnumerable<ChatInfo>, ChatListOrder, ChatListPreOrder, IReadOnlyDictionary<ChatId, Moment>?)`.

**Deviation from the spec, deliberate:** the spec says a reacted chat should sort by "the reaction's `SentAt` maxed with the chat's last-event time". That is not expressible — `ChatListOrder.ByLastEventTime` sorts by `LastTextEntry?.Version ?? Contact.Version`, a *version*, which is not comparable with a `Moment`. The implementable form with the same intent is a pre-order: reacted chats sort above the rest, ordered by reaction time descending; everything else keeps `ByLastEventTime`. Update the spec's "UI — the All tab" bullet to say this once the task is done.

- [ ] **Step 1: Write the failing test**

Create `tests/Chat.UI.Blazor.UnitTests/ChatListOrderTest.cs`:

```csharp
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatListOrderTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ReactionsFirstShouldOrderReactedChatsByReactionTime()
    {
        // arrange
        var oldChat = NewChatInfo("chat-old", version: 1);
        var busyChat = NewChatInfo("chat-busy", version: 100);
        var reactedAt = new Dictionary<ChatId, Moment> {
            [oldChat.Id] = Moment.EpochStart + TimeSpan.FromMinutes(5),
        };

        // act
        var ordered = new[] { busyChat, oldChat }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ReactionsFirst, reactedAt)
            .ToList();

        // assert
        ordered[0].Id.Should().Be(oldChat.Id, because: "a reaction must surface the chat even on an old message");
        ordered[1].Id.Should().Be(busyChat.Id);
    }

    [Fact]
    public void ReactionsFirstWithNoReactionsShouldMatchLastEventTime()
    {
        // arrange
        var older = NewChatInfo("chat-older", version: 1);
        var newer = NewChatInfo("chat-newer", version: 100);

        // act
        var ordered = new[] { older, newer }
            .OrderBy(ChatListOrder.ByLastEventTime, ChatListPreOrder.ReactionsFirst, null)
            .ToList();

        // assert
        ordered.Select(x => x.Id).Should().Equal([newer.Id, older.Id]);
    }

    private static readonly UserId OwnerId = UserId.New();

    private static ChatInfo NewChatInfo(string chatSid, long version)
    {
        var chatId = ChatId.Parse(chatSid);
        var contactId = ContactId.NewAny(OwnerId, chatId);
        return new ChatInfo(new Contact(contactId, version) {
            Chat = new Chat(chatId),
        });
    }
}
```

`Contact` is `record Contact(ContactId Id, long Version = 0)` with `Chat` as an init-only member (`src/dotnet/Api/Contacts/Contact.cs:11,49`), and `ChatInfo.Id` reads `Contact.Id.ChatId` — hence the real `ContactId` per chat rather than `ContactId.None`, which would collapse both rows onto the same id. `ChatInfo` uses referential equality, so no equality setup is needed. Drop the two `ChatId.Parse` strings in favour of ids the parser accepts if `"chat-old"` / `"chat-busy"` are rejected — any valid group chat sid works.

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~ChatListOrderTest"
```

Expected: compile error — `ChatListPreOrder.ReactionsFirst` and the 4-argument `OrderBy` do not exist.

- [ ] **Step 3: Add the pre-order value**

```csharp
namespace ActualChat.Chat;

public enum ChatListPreOrder
{
    ChatList = 0,
    None,
    NotesFirst,
    ReactionsFirst,
}
```

Appending keeps the existing ordinals, which matters — `ChatListPreOrder` is used from `ChatListSettings`-adjacent code.

- [ ] **Step 4: Add the ordering overload**

In `ChatListExt.cs`, change the `ChatInfo` `OrderBy` to take an optional map and handle the new pre-order. Keep the existing 3-argument signature working by giving the parameter a default:

```csharp
    public static IEnumerable<ChatInfo> OrderBy(
        this IEnumerable<ChatInfo> chats,
        ChatListOrder order,
        ChatListPreOrder preOrder,
        IReadOnlyDictionary<ChatId, Moment>? reactedAt = null)
    {
        var preOrderedChats = preOrder switch {
            ChatListPreOrder.ChatList => PreOrderChatListFor(chats, order),
            ChatListPreOrder.None => chats.ToFakeOrderedEnumerable(),
            ChatListPreOrder.NotesFirst => chats.OrderByDescending(c => c.Chat.SystemTag == Constants.Chat.SystemTags.Notes),
            ChatListPreOrder.ReactionsFirst => chats
                .OrderByDescending(c => GetReactedAt(reactedAt, c.Id) is not null)
                .ThenByDescending(c => GetReactedAt(reactedAt, c.Id) ?? Moment.EpochStart),
            _ => throw new ArgumentOutOfRangeException(nameof(preOrder)),
        };
        return order switch {
            ChatListOrder.ByLastEventTime => preOrderedChats
                .ThenByDescending(c => c.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByOwnUpdateTime => preOrderedChats
                .ThenByDescending(c => c.Contact.Version),
            ChatListOrder.ByUnreadCount => preOrderedChats
                .ThenByDescending(c => c.UnreadCount.Value)
                .ThenByDescending(c => c.LastTextEntry?.Version ?? c.Contact.Version),
            ChatListOrder.ByAlphabet => preOrderedChats
                .OrderByDescending(c => c.Contact.IsPinned)
                .ThenBy(c => c.Chat.Title),
            _ => throw new ArgumentOutOfRangeException(nameof(preOrder)),
        };
    }
```

Add the helper at the bottom of the class, under a `// Private methods` comment if the file does not already have one:

```csharp
    private static Moment? GetReactedAt(IReadOnlyDictionary<ChatId, Moment>? reactedAt, ChatId chatId)
        => reactedAt is not null && reactedAt.TryGetValue(chatId, out var moment) ? moment : null;
```

The `ByAlphabet` branch discards the pre-order via a fresh `OrderByDescending` — that is pre-existing behaviour, leave it.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~ChatListOrderTest"
```

Expected: 2 passed.

- [ ] **Step 6: Use it from the notifications panel**

In `ChatListUI.List`, the panel's filters are exactly the `AcrossPlace` ones, so key the pre-order off that:

```csharp
    [ComputeMethod]
    public virtual async Task<IReadOnlyList<ChatInfo>> List(
        PlaceId? placeId,
        ChatListSettings settings,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug("-> List({PlaceId}, {Settings})", placeId, settings);
        var chatById = await ListUnorderedForDisplay(placeId, settings, cancellationToken).ConfigureAwait(false);
        var filter = settings.GetFilter();
        var (preOrder, reactedAt) = filter.AcrossPlace
            ? (ChatListPreOrder.ReactionsFirst, await GetReactedAt(chatById.Keys, cancellationToken).ConfigureAwait(false))
            : (ChatListPreOrder.ChatList, null);
        DebugLog?.LogDebug(
            "<- List({PlaceId}, {Settings}): {Count} items",
            placeId, settings, chatById.Count);
        return chatById.Values.OrderBy(settings.Order, preOrder, reactedAt).ToList();
    }
```

And the private helper, in `ChatListUI`'s `// Private methods` section:

```csharp
    private async Task<IReadOnlyDictionary<ChatId, Moment>?> GetReactedAt(
        IEnumerable<ChatId> chatIds, CancellationToken cancellationToken)
    {
        var result = (Dictionary<ChatId, Moment>?)null;
        foreach (var chatId in chatIds) {
            var state = await NotificationsUI.GetReactionState(chatId, cancellationToken).ConfigureAwait(false);
            if (state.Emoji is null)
                continue;

            result ??= new Dictionary<ChatId, Moment>();
            result.Add(chatId, state.SentAt);
        }
        return result;
    }
```

- [ ] **Step 7: Build and run the UI unit tests**

```bash
dotnet build ActualChat.CI.slnf
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
```

Expected: build succeeds, all tests pass.

- [ ] **Step 8: Update the spec and commit**

Edit `docs/superpowers/specs/2026-08-30-reaction-notifications-design.md`, replacing the "Sorting:" bullet under "UI — the All tab" with the pre-order description from this task's deviation note.

```bash
git add src/dotnet docs/superpowers/specs tests/Chat.UI.Blazor.UnitTests
git commit -m "feat(ui): sort reacted chats to the top of the notifications panel"
```

---

### Task 7: The Reactions tab

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatList/ReactionNotifications/ReactionNotificationList.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatList/ReactionNotifications/ReactionNotificationItem.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatList/ReactionNotifications/reaction-notification-list.css`
- Modify: `src/dotnet/UI.Blazor.App/styles.css` (add the `@import`)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatList/NotificationsNavbarWidget.razor`
- Modify: `src/dotnet/Localization/Resources/Strings.<19 hand-written langs>.json`
- Modify: `src/dotnet/Localization/Resources/LocalizedStringsLocalizerExt.cs`

**Interfaces:**
- Consumes: `NotificationsUI.ListByKind` from Task 3; `ReactionNotification.AuthorIds` / `Emojis` from Task 1; `NotificationExt.GetChatLink`.
- Produces: `L.ChatList_TabReactions`.

**Why only one new string:** the row shows an avatar stack, the emoji, the quoted text (already composed server-side into `Notification.Text`) and a `+N` numeral for reactors beyond the avatars. None of that is prose, so the tab title is the only key. Do not compose a "Bob and 2 others" sentence in the component — that would be counted text and would need a plural key in all 19 catalogs.

- [ ] **Step 1: Add the localization key**

Add `"ChatList_TabReactions": "<translation>"` immediately after `"ChatList_TabMentions"` in each of the 19 hand-written catalogs:

| File | Value |
|---|---|
| `Strings.en.json` | `Reactions` |
| `Strings.bg.json` | `Реакции` |
| `Strings.bs.json` | `Reakcije` |
| `Strings.cs.json` | `Reakce` |
| `Strings.de.json` | `Reaktionen` |
| `Strings.es.json` | `Reacciones` |
| `Strings.fr.json` | `Réactions` |
| `Strings.hi.json` | `प्रतिक्रियाएँ` |
| `Strings.id.json` | `Reaksi` |
| `Strings.it.json` | `Reazioni` |
| `Strings.ja.json` | `リアクション` |
| `Strings.ko.json` | `반응` |
| `Strings.pl.json` | `Reakcje` |
| `Strings.pt.json` | `Reações` |
| `Strings.ru.json` | `Реакции` |
| `Strings.tr.json` | `Tepkiler` |
| `Strings.uk.json` | `Реакції` |
| `Strings.vi.json` | `Phản ứng` |
| `Strings.zh.json` | `表情回应` |

Do **not** edit `Strings.cnr.json`, `Strings.hr.json`, `Strings.sr.json` or `Strings.max.json` — they are generated. Then:

```bash
scripts/derive-bcms.cmd
scripts/derive-max.cmd
```

Add the typed member to `LocalizedStringsLocalizerExt.cs`, directly after `ChatList_TabMentions`:

```csharp
        public string ChatList_TabReactions => l["ChatList_TabReactions"].Value;
```

- [ ] **Step 2: Verify the catalogs**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj \
  --filter "FullyQualifiedName~AppLocalizationTest"
scripts/derive-bcms.cmd --check
scripts/derive-max.cmd --check
```

Expected: all pass. `AppLocalizationTest` lives in `tests/Chat.UI.Blazor.UnitTests/AppLocalizationTest.cs`.

- [ ] **Step 3: Write the item component**

Create `ReactionNotificationItem.razor`. Read `docs/ui/components.md` first. The avatar stack is `AuthorCircleGroup` (`Components/Author/AuthorCircleGroup.razor`), which already takes `IReadOnlyList<AuthorId>? AuthorIds` and collapses the overflow itself via `MaxCount` — so the component resolves no authors of its own and needs state only for the chat title:

```razor
@using ActualChat.Notifications
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, string>
@{
    var chatTitle = State.Value;
}

<div class="reaction-notification-item" @onclick="@OnClick">
    <AuthorCircleGroup
        Class="c-authors"
        AuthorIds="@Notification.AuthorIds"
        MaxCount="3"
        Size="SquareSize.Size8"
        ShowRing="false"/>
    <div class="c-body">
        <div class="c-line">
            @foreach (var emoji in Notification.Emojis) {
                <EmojiIcon Emoji="@emoji" HasPreview="false"/>
            }
            <span class="c-text">@Notification.Text</span>
        </div>
        <div class="c-meta">
            <span class="c-chat">@chatTitle</span>
            <LiveTimeDeltaText Moment="@Notification.SentAt"/>
        </div>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public ReactionNotification Notification { get; set; } = null!;

    private IChats Chats => Hub.Chats;

    protected override ComputedState<string>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<string>.Options() {
                InitialValue = "",
                UpdateDelayer = FixedDelayer.Get(0.1),
                Category = GetStateCategory(t),
            });

    protected override async Task<string> ComputeState(CancellationToken cancellationToken) {
        var chat = await Chats.Get(Session, Notification.ChatId, cancellationToken).ConfigureAwait(false);
        return chat?.Title ?? "";
    }

    private void OnClick()
        => _ = Hub.History.NavigateTo(Notification.GetChatLink());
}
```

`ApiArray<T>` implements `IReadOnlyList<T>` (`ActualLab.Core/Api/ApiArray.cs:36`), so `AuthorIds="@Notification.AuthorIds"` binds directly — no `ToList()`.

- [ ] **Step 4: Write the list component**

Create `ReactionNotificationList.razor`:

```razor
@using ActualChat.Notifications
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, ReactionNotificationList.Model>
@{
    var m = State.Value;
}

<div class="reaction-notification-list">
    @foreach (var notification in m.Notifications) {
        <ReactionNotificationItem @key="@notification.Id.Value" Notification="@notification"/>
    }
</div>

@code {
    private NotificationsUI NotificationsUI => Hub.NotificationsUI;

    protected override ComputedState<Model>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<Model>.Options() {
                InitialValue = new(),
                UpdateDelayer = FixedDelayer.Get(0.1),
                Category = GetStateCategory(t),
            });

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var notifications = await NotificationsUI
            .ListByKind(NotificationKind.Reaction, cancellationToken)
            .ConfigureAwait(false);
        return new(notifications.OfType<ReactionNotification>().ToList());
    }

    // Nested types

    public sealed record Model(IReadOnlyList<ReactionNotification> Notifications = null!)
    {
        public IReadOnlyList<ReactionNotification> Notifications { get; init; } = Notifications ?? [];
    }
}
```

- [ ] **Step 5: Write the CSS and register it**

Create `reaction-notification-list.css`. Match the conventions in `chat-list.css` — kebab-case root class, `c-` prefixed children:

```css
.reaction-notification-list {
    @apply flex-y;
}

.reaction-notification-item {
    @apply flex-x items-start gap-x-2;
    @apply px-3 py-2;
    @apply cursor-pointer;
}

.reaction-notification-item .c-authors {
    @apply shrink-0;
}

.reaction-notification-item .c-body {
    @apply flex-y min-w-0;
}

.reaction-notification-item .c-line {
    @apply flex-x items-center gap-x-1;
}

.reaction-notification-item .c-text {
    @apply truncate;
}

.reaction-notification-item .c-meta {
    @apply flex-x items-center gap-x-2;
    @apply text-xs text-03;
}
```

Confirm `flex-x` / `flex-y` / `text-03` exist by grepping `src/dotnet/UI.Blazor.App/styles.css` and `chat-list.css`; substitute the real utility names if they differ.

Add to `src/dotnet/UI.Blazor.App/styles.css`, next to the other `ChatList` imports:

```css
@import "Components/ChatList/ReactionNotifications/reaction-notification-list.css";
```

- [ ] **Step 6: Add the tab**

In `NotificationsNavbarWidget.razor`: add the filter-independent tab. Because the Reactions tab is not backed by a `ChatListFilter`, give it its own id and branch in the content swap.

```csharp
    private const string ReactionsTabId = "@reactions";
```

In the `@{ }` block, after the three existing tabs:

```razor
    if (m.HasReactions)
        tabs.Add(new TabDef(ReactionsTabId, L.ChatList_TabReactions) {
            Class = "chats-tab",
            TitleContent = @<text><span>@L.ChatList_TabReactions</span></text>,
        });
```

Change the empty-state guard and the content swap:

```razor
        @if (!m.HasAll && !m.HasPeople && !m.HasMentions && !m.HasReactions) {
```

```razor
                <ChildContent>
                    @if (_settings.FilterId == ReactionsTabId) {
                        <ReactionNotificationList/>
                    }
                    else {
                        <ChatList
                            @key="@ChatList.GetKey(null, false, _settings)"
                            PlaceId="@null"
                            UsePlaceChatListSettings="@false"
                            Settings="@_settings"/>
                    }
                </ChildContent>
```

Extend the model and `ComputeState`:

```csharp
    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var hasAll = await HasTab(ChatListFilter.Unread, cancellationToken).ConfigureAwait(false);
        var hasPeople = await HasTab(ChatListFilter.UnreadPeople, cancellationToken).ConfigureAwait(false);
        var hasMentions = await HasTab(ChatListFilter.UnreadMentions, cancellationToken).ConfigureAwait(false);
        var reactedChatIds = await NotificationsUI.ListReactedChatIds(cancellationToken).ConfigureAwait(false);
        return new(hasAll, hasPeople, hasMentions, reactedChatIds.Count > 0);
    }
```

```csharp
    public sealed record Model(
        bool HasAll = false,
        bool HasPeople = false,
        bool HasMentions = false,
        bool HasReactions = false);
```

`GetTabIndex` drives the swipe direction and is keyed off the `Filters` array; add the reactions id so a swipe onto the new tab animates in the right direction:

```csharp
    private static int GetTabIndex(Symbol filterId) {
        for (var i = 0; i < Filters.Length; i++)
            if (Filters[i].Id == filterId)
                return i;
        if (filterId == ReactionsTabId)
            return Filters.Length;

        return 0;
    }
```

- [ ] **Step 7: Regenerate the AOT component registrations**

The two new components must be discoverable under Native AOT:

```bash
./run-aot-type-generator.cmd
git diff --stat src/dotnet/UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs
```

Expected: the diff adds `ReactionNotificationList` and `ReactionNotificationItem` keeps.

- [ ] **Step 8: Build and verify**

```bash
dotnet build ActualChat.CI.slnf
dotnet test tests/UI.Blazor.UnitTests/UI.Blazor.UnitTests.csproj
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
```

Expected: build succeeds, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/dotnet docs
git commit -m "feat(ui): add the Reactions tab to the notifications panel"
```

---

### Task 8: End-to-end verification in the running app

**Files:** none — this is a manual gate before the branch is considered done.

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: nothing.

**Why this task exists:** two of the spec's test requirements — badge precedence (`@` over count over emoji) and a reaction-only chat appearing in All — live in a `.razor` `@{ }` block and in Fusion-wired list membership. Covering them automatically would mean standing up bUnit and a hub, which this codebase does not do for chat-list components. They are verified here instead, deliberately, rather than left unverified.

- [ ] **Step 1: Make sure the server is up**

The host runs `./run-watch.cmd`, which rebuilds and restarts on file changes. Poll until ready:

```bash
tail -n 40 tmp/watch-dotnet.log
```

Expected: `Now listening on:`. If you see `error`, fix it and wait again. Do not use `/server-start` or `/server-restart` — the watch process owns the server.

- [ ] **Step 2: Drive the UI**

Use the `/debug-ui` skill to sign in as two users in one browser session. As user A, post a message in a shared chat. As user B, react to it with 👍.

- [ ] **Step 3: Check each surface as user A**

Confirm, in order:
1. The navbar bell appears even though nothing is unread.
2. The All tab lists the chat with the 👍 badge, at the top of the list.
3. The Reactions tab exists and shows one row: B's avatar, 👍, the quoted message, the chat title, a relative time.
4. Clicking the row navigates to the reacted message.
5. Once the message is on screen, the row disappears and the bell goes out — `SeenNotificationDismisser` clearing an on-view notification. This is expected behaviour, not a bug.

- [ ] **Step 4: Check accumulation**

Repeat with a third user C reacting 🎉 to the same message of A's, before A opens the chat. The single Reactions row should show two avatars and both emoji, and A should have received one banner per reaction rather than a re-alert for the same one.

- [ ] **Step 5: Check the mention precedence**

In a chat where A has both an unread @-mention and a reaction, the All row must show `@`, not the emoji.

- [ ] **Step 6: Commit any fixes and finish**

If any step failed, fix it, re-verify, and commit. When all six pass, the feature is done. Ask the developer whether to run the `/prepare-merge` skill before opening a PR.

---

## Notes for the reviewer

One thing in this plan is an informed guess the implementer must confirm rather than take on faith: the Tailwind utility names in the new CSS (Task 7 Step 5). That step says so at the point it matters.

Everything else — file paths, line numbers, wire keys, emoji constants, component parameter names, test-project references — was read from the tree at the time of writing. Two facts worth re-checking if a step misbehaves, because they are the load-bearing ones:

- `ReactionNotification` keys 9 and 10 are free *within that subtype*; `CallNotification` (9) and `ConversationNotification` (9, 10) already reuse the range because union members serialize independently. Task 2 is the gate on this.
- Reaction state deliberately never lands on `ChatInfo` — see `ChatUI.cs:160` for why per-row state is kept out of `Get()`. Tasks 4 and 5 both route around it; if a step seems to want a `ChatInfo` field, the step is being misread.
