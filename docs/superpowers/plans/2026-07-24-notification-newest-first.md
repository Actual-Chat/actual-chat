# Newest-First Coalesced Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Coalesced chat notifications show the newest messages first (not the stale first-unread + counter), and Android renders a true per-message `MessagingStyle` transcript.

**Architecture:** A structured `RecentMessages` list (capped at 5, oldest→newest) on `ChatEntryRelatedNotification` becomes the single source of truth: `MergeWith` maintains it, the server composes the newest-first body string from it for every platform, and ships it as a compact JSON data key so the Android client builds a real `MessagingStyle`. Spec: `docs/superpowers/specs/2026-07-24-notification-newest-first-design.md`.

**Tech Stack:** .NET 10, MessagePack contracts (`ApiArray`), System.Text.Json (push wire), FirebaseAdmin (FCM), AndroidX `NotificationCompat.MessagingStyle` (MAUI Android).

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any C#.** Highlights that WILL bite you: no `Async` suffix; no XML docs on members (type-level `///` only when the name isn't self-explanatory, ≤5 lines); `//` comments only for non-obvious invariants, placed at top of method body; Allman braces for types/methods, K&R for everything else; 120-char lines; LF endings; `x.IsNullOrEmpty()` over `string.IsNullOrEmpty(x)`; no `StringComparison.Ordinal` (invariant globalization); test names PascalCase without underscores, AAA pattern with lowercase `// arrange` / `// act` / `// assert` comments.
- `System.Text.Json`, `System.Text.Json.Serialization`, `ActualChat.Compliance`, `ActualLab.*` are **global usings** — do not add explicit `using` directives for them.
- Build with `ActualChat.CI.slnf` (excludes MAUI projects — no Android workload in this environment). `src/dotnet/App.Maui` **cannot be built here**; Android changes are verified by review + the user's host build.
- MessagePack key 18 is the next free key in the `Notification` → `ChatNotification` → `ChatEntryRelatedNotification` chain (base uses 0–7 + 16, `ChatNotification` uses 8, `ChatEntryRelatedNotification` uses 9–15 + 17; `Message/Reply/ThreadNotification` add none).
- `LeadText`/`LeadCount` stay on the wire this release (rolling-deploy compat): new code writes `LeadText` = newest message text, `LeadCount` = 1. Do not delete them.
- Commit after each task; messages use conventional-commit prefixes (`feat(notifications): …`, `test(notifications): …`). Do NOT push.
- Test suite command (fast, no infra needed for these tests):
  `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest" 2>&1 | tail -20`

---

### Task 1: `NotificationMessage` contract + constants

**Files:**
- Create: `src/dotnet/Api/Notifications/NotificationMessage.cs`
- Modify: `src/dotnet/Api/Constants.cs` (Notification class, ~line 319: replace `LeadRollInThreshold`)
- Test: `tests/Notifications.IntegrationTests/NotificationMessageTest.cs` (new)

**Interfaces:**
- Consumes: `Constants.Notification` (existing), `Sanitizer.MaskPrivate` (global `ActualChat.Compliance`), `Moment`, `AuthorId`.
- Produces: `NotificationMessage` record with properties `AuthorId AuthorId`, `string AuthorName`, `string Text`, `long EntryLid`, `Moment SentAt`; factory `NotificationMessage.New(AuthorId, string, string, long, Moment)` (truncates text); constants `Constants.Notification.MaxRecentMessages = 5`, `Constants.Notification.MaxRecentMessageTextLength = 200`. Later tasks call exactly these.

- [ ] **Step 1: Write the failing tests**

Create `tests/Notifications.IntegrationTests/NotificationMessageTest.cs`:

```csharp
namespace ActualChat.Notifications.IntegrationTests;

public class NotificationMessageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void NewTruncatesLongText()
    {
        var text = new string('x', 300);

        var message = NotificationMessage.New(AuthorId.New(TestChatId, 1), "Alice", text, 100, Moment.Now);

        message.Text.Length.Should().Be(Constants.Notification.MaxRecentMessageTextLength);
        message.Text.Should().EndWith("…");
    }

    [Fact]
    public void NewKeepsShortTextVerbatim()
    {
        var message = NotificationMessage.New(AuthorId.New(TestChatId, 1), "Alice", "hello", 100, Moment.Now);

        message.Text.Should().Be("hello");
        message.AuthorName.Should().Be("Alice");
        message.EntryLid.Should().Be(100);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationMessageTest" 2>&1 | tail -20`
Expected: build FAILURE — `NotificationMessage` / `MaxRecentMessageTextLength` do not exist.

- [ ] **Step 3: Add the constants**

In `src/dotnet/Api/Constants.cs`, `public static class Notification`, replace:

```csharp
        // A first unread message shorter than this rolls the next message into the notification lead.
        public const int LeadRollInThreshold = 24;
```

with:

```csharp
        // Messages kept verbatim in a coalesced notification (its transcript window); older ones
        // fold into the "+N earlier messages" tail.
        public const int MaxRecentMessages = 5;
        public const int MaxRecentMessageTextLength = 200;
```

Note: `LeadRollInThreshold`'s only usage is in `ChatEntryRelatedNotification.MergeWith`, which Task 2 rewrites. To keep the build green within this task, update that usage now as part of Step 3 — in `src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs:78` change `Constants.Notification.LeadRollInThreshold` to `Constants.Notification.MaxRecentMessageTextLength` (temporary; the whole block is deleted in Task 2).

- [ ] **Step 4: Create the record**

Create `src/dotnet/Api/Notifications/NotificationMessage.cs`:

```csharp
namespace ActualChat.Notifications;

/// <summary>
/// One message inside a coalesced chat notification: a display-ready snapshot (author name
/// resolved at send time) kept in <see cref="ChatEntryRelatedNotification.RecentMessages"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationMessage : ISanitized
{
    [DataMember(Order = 0), Key(0)]
    public AuthorId AuthorId { get; init; }
    [DataMember(Order = 1), Key(1)]
    public string AuthorName { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 2), Key(2)]
    public string Text { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember(Order = 3), Key(3)]
    public long EntryLid { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment SentAt { get; init; }

    public static NotificationMessage New(
        AuthorId authorId, string authorName, string text,
        long entryLid, Moment sentAt)
        => new() {
            AuthorId = authorId,
            AuthorName = authorName,
            Text = Truncate(text),
            EntryLid = entryLid,
            SentAt = sentAt,
        };

    // Private methods

    private static string Truncate(string text)
        => text.Length <= Constants.Notification.MaxRecentMessageTextLength
            ? text
            : text[..(Constants.Notification.MaxRecentMessageTextLength - 1)] + "…";
}
```

The sanitized getters (`Sanitizer.MaskPrivate(field)`) mirror `Notification.Title`/`Text` — message bodies must not leak into sanitized logs. This is why the record is property-style, not positional.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationMessageTest" 2>&1 | tail -20`
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Notifications/NotificationMessage.cs src/dotnet/Api/Constants.cs src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs tests/Notifications.IntegrationTests/NotificationMessageTest.cs
git commit -m "feat(notifications): NotificationMessage contract + recent-messages constants"
```

---

### Task 2: `RecentMessages` merge logic

**Files:**
- Modify: `src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs` (field + full `MergeWith` rewrite)
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs:999-1006` (creation seed)
- Test: `tests/Notifications.IntegrationTests/NotificationAggregationTest.cs`

**Interfaces:**
- Consumes: `NotificationMessage`, `NotificationMessage.New(...)`, `Constants.Notification.MaxRecentMessages` (Task 1).
- Produces: `ChatEntryRelatedNotification.RecentMessages` — `ApiArray<NotificationMessage>`, ordered oldest→newest, ≤5 entries, MessagePack `Key(18)`/`Order 18`. After any merge: `LeadText` == newest message's `Text`, `LeadCount` == 1, `Title`/`IconUrl` belong to the max-`EntryLid` message. Tasks 3–5 read `RecentMessages` exactly as described.

- [ ] **Step 1: Update the test helper and existing merge tests to the new contract**

In `tests/Notifications.IntegrationTests/NotificationAggregationTest.cs`:

Replace the `NewMessage` helper (lines 297–305) with:

```csharp
    private static MessageNotification NewMessage(long entryLid, AuthorId authorId, string text, string authorName = "")
        => MessageNotification.New(TestUserId, TestChatId, entryLid, authorId) with {
            Text = text,
            StartEntryLid = entryLid,
            UnreadCount = 1,
            AuthorIds = new[] { authorId }.ToApiArray(),
            RecentMessages = new[] {
                NotificationMessage.New(authorId, authorName, text, entryLid, Moment.EpochStart + TimeSpan.FromSeconds(entryLid)),
            }.ToApiArray(),
            LeadText = text,
            LeadCount = 1,
        };
```

Replace `MergeAccumulatesUnreadAndAuthors` (lines 84–103) with:

```csharp
    [Fact]
    public void MergeAccumulatesUnreadAndAuthors()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var first = NewMessage(100, author1, "Hey team, here is the long first message");
        var second = NewMessage(101, author2, "second");

        var info = new UserNotificationInfo(TestUserId)
            .WithNotification(first)
            .WithNotification(second);

        var merged = info.Displayed.Single().Should().BeOfType<MessageNotification>().Subject;
        merged.StartEntryLid.Should().Be(100);
        merged.EntryLid.Should().Be(101);
        merged.StartEntryId.Should().Be(ChatEntryId.New(TestChatId, 100));
        merged.UnreadCount.Should().Be(2);
        merged.AuthorIds.Should().BeEquivalentTo(new[] { author1, author2 });
        merged.RecentMessages.Select(m => m.Text)
            .Should().Equal("Hey team, here is the long first message", "second");
        merged.LeadText.Should().Be("second");
        merged.LeadCount.Should().Be(1);
    }
```

Replace `MergeRollsInShortFirstMessage` (lines 105–121) with:

```csharp
    [Fact]
    public void MergeEvictsOldestBeyondCapacity()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 107; lid++)
            info = info.WithNotification(NewMessage(lid, author1, $"m{lid}"));

        var merged = (MessageNotification)info.Displayed.Single();
        merged.UnreadCount.Should().Be(7);
        merged.RecentMessages.Should().HaveCount(Constants.Notification.MaxRecentMessages);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("m102", "m103", "m104", "m105", "m106");
        merged.StartEntryLid.Should().Be(100);
        merged.LeadText.Should().Be("m106");
    }
```

In `MergeIsIdempotentOnRedelivery` (lines 123–143), replace the three lead assertions:

```csharp
        merged.LeadText.Should().Be("Hi\nare you there?");
        merged.LeadCount.Should().Be(2);
```

with:

```csharp
        merged.RecentMessages.Select(m => m.Text).Should().Equal("Hi", "are you there?");
        merged.LeadText.Should().Be("are you there?");
        merged.LeadCount.Should().Be(1);
```

Replace `MergeKeepsNewestSentAtOnOutOfOrderMerge` (lines 145–162) with:

```csharp
    [Fact]
    public void MergeKeepsNewestSentAtAndTitleOnOutOfOrderMerge()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var t0 = Moment.Now;
        var t1 = t0 + TimeSpan.FromSeconds(30);
        var existing = NewMessage(101, author1, "second") with { SentAt = t1, Title = "Bob @ Chat" };
        var late = NewMessage(100, author1, "first") with { SentAt = t0, Title = "Alice @ Chat" };

        // The delayed earlier message extends the window, but must regress neither the timestamp
        // (a regressed SentAt would fake a lull) nor the headline (title tracks the newest message).
        var merged = (MessageNotification)late.MergeWith(existing);
        merged.SentAt.Should().Be(t1);
        merged.Title.Should().Be("Bob @ Chat");
        merged.StartEntryLid.Should().Be(100);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("first", "second");
        merged.LeadText.Should().Be("second");
        merged.UnreadCount.Should().Be(2);
    }
```

Replace `MergeUpgradesLegacyNotification` (lines 217–230) with:

```csharp
    [Fact]
    public void MergeUpgradesLegacyNotification()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        // A pre-RecentMessages blob deserializes with an empty list; its text lives in LeadText/Text.
        var legacy = MessageNotification.New(TestUserId, TestChatId, 100, author1) with { Text = "old text" };
        var incoming = NewMessage(101, author1, "new text");

        var merged = (MessageNotification)incoming.MergeWith(legacy);
        merged.UnreadCount.Should().Be(2);
        merged.StartEntryLid.Should().Be(100);
        merged.RecentMessages.Select(m => m.Text).Should().Equal("old text", "new text");
        merged.RecentMessages[0].AuthorName.Should().Be("");
        merged.LeadText.Should().Be("new text");
    }
```

In `AggregatedNotificationRoundtrips` (lines 246–274), add to the record initializer after `LeadCount = 2,`:

```csharp
            RecentMessages = new[] {
                NotificationMessage.New(author1, "Alice", "Lead", 100, Moment.Now),
                NotificationMessage.New(author2, "Bob", "Body", 101, Moment.Now),
            }.ToApiArray(),
```

and add assertions before the whole-record compare, replacing the final compare line:

```csharp
        result.RecentMessages.Should().BeEquivalentTo(n.RecentMessages);
        // ApiArray equality is by reference, so normalize them before the whole-record value compare.
        result.Should().Be(n with { AuthorIds = result.AuthorIds, RecentMessages = result.RecentMessages });
```

Leave `MergeResetsBeepBackoffAfterLull`, `MergePreservesBeepStateAndStartAnchor`, and all mention/call/beep tests untouched.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest" 2>&1 | tail -20`
Expected: build FAILURE — `RecentMessages` does not exist.

- [ ] **Step 3: Add the field and rewrite `MergeWith`**

In `src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs`:

Replace the `LeadText`/`LeadCount` member comments and add the new field. Lines 22–33 become:

```csharp
    // Rolling-deploy compat only: old nodes/clients compose from LeadText (= newest message text)
    // and LeadCount (= 1). New code reads RecentMessages. Remove both in the next release.
    [DataMember(Order = 13), Key(13)]
    public string LeadText { get; init; } = "";
    [DataMember(Order = 14), Key(14)]
    public int BeepCount { get; init; }
    [DataMember(Order = 15), Key(15)]
    public Moment LastBeepAt { get; init; }
    [DataMember(Order = 17), Key(17)]
    public int LeadCount { get; init; }
    // The transcript window: last MaxRecentMessages unread messages, oldest -> newest.
    [DataMember(Order = 18), Key(18)]
    public ApiArray<NotificationMessage> RecentMessages { get; init; }
```

Replace `MergeWith` (lines 41–103) and add the two private helpers below `MinPositive`:

```csharp
    public override Notification MergeWith(Notification? existing)
    {
        if (existing is not ChatEntryRelatedNotification e)
            return base.MergeWith(existing);

        // Notification events can be processed out of order, so anchor at the min (earliest) unread
        // entry and track the max (latest) — don't assume the existing one arrived first.
        var existingStart = e.StartEntryLid > 0 ? e.StartEntryLid : e.EntryLid;
        var incomingStart = StartEntryLid > 0 ? StartEntryLid : EntryLid;
        // An entry already inside the merged window is a redelivery (the queue is at-least-once),
        // so the merge must be idempotent: return the existing instance unchanged — the caller
        // relies on reference equality to skip the beep/push for no-op merges.
        if (EntryLid > 0 && EntryLid <= e.EntryLid && incomingStart >= existingStart)
            return e;

        var authorIds = e.AuthorIds;
        if (AuthorId is { } authorId && !authorIds.Contains(authorId) && authorIds.Count < Constants.Notification.MaxTrackedAuthors)
            authorIds = authorIds.With(authorId);
        var startEntryLid = MinPositive(existingStart, incomingStart);
        var entryLid = Math.Max(e.EntryLid, EntryLid);
        // Pre-coalescing blobs deserialize UnreadCount as 0 though they represent one unread entry.
        var existingUnread = Math.Max(1, e.UnreadCount);
        var recentMessages = MergeRecentMessages(e, this);
        var newestIsIncoming = EntryLid >= e.EntryLid;

        // A gap between messages long enough to count as a conversation lull resets the beep
        // back-off, so this fresh message alerts immediately instead of inheriting the back-off.
        var isLull = SentAt - e.SentAt >= Constants.Notification.BeepResetPeriod;
        return this with {
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            HandledAt = null,
            // An out-of-order earlier message must not regress the newest-activity timestamp,
            // and the banner headline (title/icon) must keep tracking the newest message.
            SentAt = Moment.Max(e.SentAt, SentAt),
            Title = newestIsIncoming ? Title : e.Title,
            IconUrl = newestIsIncoming ? IconUrl : e.IconUrl,
            EntryLid = entryLid,
            StartEntryLid = startEntryLid,
            UnreadCount = existingUnread + 1,
            AuthorIds = authorIds,
            RecentMessages = recentMessages,
            LeadText = recentMessages.IsEmpty ? "" : recentMessages[^1].Text,
            LeadCount = 1,
            BeepCount = isLull ? 0 : e.BeepCount,
            LastBeepAt = isLull ? default : e.LastBeepAt,
        };
    }

    private static long MinPositive(long a, long b)
        => a <= 0 ? b : b <= 0 ? a : Math.Min(a, b);

    private static ApiArray<NotificationMessage> MergeRecentMessages(
        ChatEntryRelatedNotification existing, ChatEntryRelatedNotification incoming)
    {
        var messages = new List<NotificationMessage>(existing.RecentMessages.Count + 1);
        messages.AddRange(GetRecentMessages(existing));
        foreach (var message in GetRecentMessages(incoming))
            if (messages.All(m => m.EntryLid != message.EntryLid))
                messages.Add(message);
        messages.Sort((a, b) => a.EntryLid.CompareTo(b.EntryLid));
        if (messages.Count > Constants.Notification.MaxRecentMessages)
            messages.RemoveRange(0, messages.Count - Constants.Notification.MaxRecentMessages);
        return messages.ToApiArray();
    }

    // Pre-RecentMessages blobs carry their text in LeadText/Text; synthesize a message so the
    // merge upgrades them in place (empty author name -> the line renders without a prefix).
    private static IEnumerable<NotificationMessage> GetRecentMessages(ChatEntryRelatedNotification n)
    {
        if (!n.RecentMessages.IsEmpty)
            return n.RecentMessages;

        var text = n.LeadText.IsNullOrEmpty() ? n.Text : n.LeadText;
        return text.IsNullOrEmpty()
            ? []
            : [NotificationMessage.New(n.AuthorId ?? default, "", text, n.EntryLid, n.SentAt)];
    }
```

(The old roll-in block referencing `MaxRecentMessageTextLength` from Task 1's temporary edit is gone.)

- [ ] **Step 4: Seed `RecentMessages` at creation**

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`, `EnqueueMessageRelatedNotifications` (lines 999–1006), replace:

```csharp
            if (notification is ChatEntryRelatedNotification related)
                notification = related with {
                    StartEntryLid = entryLid,
                    UnreadCount = 1,
                    AuthorIds = new[] { changeAuthor.Id }.ToApiArray(),
                    LeadText = content,
                    LeadCount = 1,
                };
```

with:

```csharp
            if (notification is ChatEntryRelatedNotification related)
                notification = related with {
                    StartEntryLid = entryLid,
                    UnreadCount = 1,
                    AuthorIds = new[] { changeAuthor.Id }.ToApiArray(),
                    RecentMessages = new[] {
                        NotificationMessage.New(changeAuthor.Id, changeAuthor.Avatar.Name, content, entryLid, now),
                    }.ToApiArray(),
                    LeadText = content,
                    LeadCount = 1,
                };
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest" 2>&1 | tail -20`
Expected: all pass (including untouched beep/mention tests).

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs src/dotnet/Notifications.Service/NotificationsBackend.cs tests/Notifications.IntegrationTests/NotificationAggregationTest.cs
git commit -m "feat(notifications): maintain RecentMessages transcript window in coalescing merge"
```

---

### Task 3: Newest-first text composition + `ReAnchor`

**Files:**
- Modify: `src/dotnet/Notifications.Service/NotificationHelper.cs:45-56` (replace `GetAggregatedText`)
- Modify: `src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs` (add `ReAnchorAt`)
- Modify: `src/dotnet/Notifications.Service/NotificationsBackend.cs:1338,1377-1418` (call sites, `ReAnchor`, delete old `ComposeAggregatedText`)
- Test: `tests/Notifications.IntegrationTests/NotificationAggregationTest.cs`

**Interfaces:**
- Consumes: `ChatEntryRelatedNotification.RecentMessages`, `LeadText` (Task 2), `ChatId.GetThreadOutermostParentOrSelf().Kind`, `ChatKind`.
- Produces: `NotificationHelper.ComposeAggregatedText(ChatEntryRelatedNotification notification)` → `string`, pure/static. Format: newest message first, one line per message, `AuthorName: ` prefixes only in Group/Place chats, `+N earlier message(s)` tail when `UnreadCount > RecentMessages.Count`, falls back to `LeadText`/`Text` when `RecentMessages` is empty. Also `ChatEntryRelatedNotification.ReAnchorAt(long newStart)` — the pure part of partial-read re-anchoring (filters the window, recomputes `UnreadCount`/`LeadText`).

- [ ] **Step 1: Write the failing tests**

In `tests/Notifications.IntegrationTests/NotificationAggregationTest.cs`, replace `AggregatedTextCountsOnlyMessagesBeyondLead` (lines 209–215) with:

```csharp
    [Fact]
    public void AggregatedTextIsNewestFirstWithAuthorPrefixesInGroupChat()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var author2 = AuthorId.New(TestChatId, 2);
        var first = NewMessage(100, author1, "who takes the release?", "Alice");
        var second = NewMessage(101, author2, "I fixed the flaky test", "Bob");

        var merged = (MessageNotification)second.MergeWith(first);

        NotificationHelper.ComposeAggregatedText(merged)
            .Should().Be("Bob: I fixed the flaky test\nAlice: who takes the release?");
    }

    [Fact]
    public void AggregatedTextSkipsAuthorNamesInPeerChat()
    {
        var peerChatId = PeerChatId.New(UserId.New(), UserId.New());
        var author = AuthorId.New(peerChatId, 1);
        var first = MessageNotification.New(TestUserId, peerChatId, 100, author) with {
            StartEntryLid = 100,
            UnreadCount = 1,
            RecentMessages = new[] { NotificationMessage.New(author, "Alice", "first", 100, Moment.Now) }.ToApiArray(),
        };
        var second = MessageNotification.New(TestUserId, peerChatId, 101, author) with {
            StartEntryLid = 101,
            UnreadCount = 1,
            RecentMessages = new[] { NotificationMessage.New(author, "Alice", "second", 101, Moment.Now) }.ToApiArray(),
        };

        var merged = (MessageNotification)second.MergeWith(first);

        NotificationHelper.ComposeAggregatedText(merged).Should().Be("second\nfirst");
    }

    [Fact]
    public void AggregatedTextCountsOnlyMessagesBeyondWindow()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 106; lid++)
            info = info.WithNotification(NewMessage(lid, author1, $"m{lid}", "Alice"));
        var merged = (MessageNotification)info.Displayed.Single();

        var text = NotificationHelper.ComposeAggregatedText(merged);

        text.Should().StartWith("Alice: m105");
        text.Should().EndWith("+1 earlier message");
        merged.UnreadCount.Should().Be(6);
    }

    [Fact]
    public void AggregatedTextFallsBackForLegacyNotification()
    {
        var author1 = AuthorId.New(TestChatId, 1);
        var legacy = MessageNotification.New(TestUserId, TestChatId, 100, author1) with {
            Text = "composed old body",
            LeadText = "old lead",
        };

        NotificationHelper.ComposeAggregatedText(legacy).Should().Be("old lead");
    }

    [Fact]
    public void ReAnchorAtDropsReadMessages()
    {
        var author = AuthorId.New(TestChatId, 1);
        var info = new UserNotificationInfo(TestUserId);
        for (var lid = 100; lid < 105; lid++)
            info = info.WithNotification(NewMessage(lid, author, $"m{lid}", "Alice"));
        var merged = (MessageNotification)info.Displayed.Single();

        var reAnchored = merged.ReAnchorAt(103);

        reAnchored.StartEntryLid.Should().Be(103);
        reAnchored.UnreadCount.Should().Be(2);
        reAnchored.RecentMessages.Select(m => m.Text).Should().Equal("m103", "m104");
        reAnchored.LeadText.Should().Be("m104");
    }

    [Fact]
    public void ReAnchorAtEmptiesWindowWhenAllShownAreRead()
    {
        var author = AuthorId.New(TestChatId, 1);
        var n = NewMessage(100, author, "only", "Alice");

        var reAnchored = n.ReAnchorAt(101);

        reAnchored.RecentMessages.Should().BeEmpty();
        reAnchored.LeadText.Should().Be("");
        reAnchored.UnreadCount.Should().Be(1);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest" 2>&1 | tail -20`
Expected: build FAILURE — `NotificationHelper.ComposeAggregatedText` / `ReAnchorAt` do not exist.

- [ ] **Step 3: Add `ReAnchorAt` to the record**

In `src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs`, add after `MergeWith` (before the `// Private methods`-area helpers):

```csharp
    // Moves the first-unread anchor to newStart: drops now-read messages from the transcript
    // window and approximates the remaining unread count from the entry span. The caller re-seeds
    // RecentMessages (from the entry store) when the window empties, then recomposes Text.
    public ChatEntryRelatedNotification ReAnchorAt(long newStart)
    {
        var recentMessages = RecentMessages.Where(m => m.EntryLid >= newStart).ToApiArray();
        return this with {
            StartEntryLid = newStart,
            UnreadCount = (int)Math.Max(1, EntryLid - newStart + 1),
            RecentMessages = recentMessages,
            LeadText = recentMessages.IsEmpty ? "" : recentMessages[^1].Text,
            LeadCount = 1,
        };
    }
```

- [ ] **Step 4: Replace `GetAggregatedText` with `ComposeAggregatedText`**

In `src/dotnet/Notifications.Service/NotificationHelper.cs`, replace `GetAggregatedText` (lines 45–56) with:

```csharp
    public static string ComposeAggregatedText(ChatEntryRelatedNotification notification)
    {
        var messages = notification.RecentMessages;
        if (messages.IsEmpty)
            return notification.LeadText.IsNullOrEmpty() ? notification.Text : notification.LeadText;

        // Newest first: collapsed banners show only the first line(s), and that must be the
        // latest message, not the oldest unread one.
        var showAuthorNames = notification.ChatId.GetThreadOutermostParentOrSelf().Kind
            is ChatKind.Group or ChatKind.Place;
        var lines = new List<string>(messages.Count + 1);
        for (var i = messages.Count - 1; i >= 0; i--) {
            var m = messages[i];
            lines.Add(showAuthorNames && !m.AuthorName.IsNullOrEmpty() ? $"{m.AuthorName}: {m.Text}" : m.Text);
        }
        var moreCount = notification.UnreadCount - messages.Count;
        if (moreCount > 0)
            lines.Add(moreCount == 1 ? "+1 earlier message" : $"+{moreCount} earlier messages");
        return string.Join('\n', lines);
    }
```

- [ ] **Step 5: Update the backend call sites and `ReAnchor`; delete the old resolver**

In `src/dotnet/Notifications.Service/NotificationsBackend.cs`:

Line 1338, replace:

```csharp
                var text = await ComposeAggregatedText(related, cancellationToken).ConfigureAwait(false);
```

with:

```csharp
                var text = NotificationHelper.ComposeAggregatedText(related);
```

Replace `ReAnchor` (lines 1375–1400) with:

```csharp
    // Partial read: drops now-read messages via ReAnchorAt; when every shown message was read,
    // re-seeds the window from the first still-unread entry so the banner isn't left textless.
    private async Task<ChatEntryRelatedNotification> ReAnchor(
        ChatEntryRelatedNotification related, long newStart, CancellationToken cancellationToken)
    {
        var updated = related.ReAnchorAt(newStart);
        if (updated.RecentMessages.IsEmpty) {
            var entry = await ChatsBackend
                .GetEntry(ChatEntryId.New(related.ChatId, newStart), TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            if (entry is { IsSystemEntry: false }) {
                var (text, _) = await NotificationHelper
                    .GetText(entry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
                    .ConfigureAwait(false);
                var author = await AuthorsBackend
                    .Get(related.ChatId, entry.AuthorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
                var message = NotificationMessage.New(
                    entry.AuthorId, author?.Avatar.Name ?? "", text, entry.LocalId, entry.BeginsAt);
                updated = updated with {
                    RecentMessages = new[] { message }.ToApiArray(),
                    LeadText = message.Text,
                };
            }
        }
        return updated with { Text = NotificationHelper.ComposeAggregatedText(updated) };
    }
```

Delete the old private `ComposeAggregatedText` method (lines 1402–1418) entirely — the author-name resolution loop is obsolete (names are snapshots on the messages now).

- [ ] **Step 6: Run tests + build the service**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest" 2>&1 | tail -20`
Expected: all pass.
Run: `dotnet build src/dotnet/Notifications.Service/Notifications.Service.csproj 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Notifications/ChatEntryRelatedNotification.cs src/dotnet/Notifications.Service/NotificationHelper.cs src/dotnet/Notifications.Service/NotificationsBackend.cs tests/Notifications.IntegrationTests/NotificationAggregationTest.cs
git commit -m "feat(notifications): compose newest-first notification body from RecentMessages"
```

---

### Task 4: Push payload — `PushMessage` JSON + `messages` data key

**Files:**
- Create: `src/dotnet/Api/Notifications/PushMessage.cs`
- Modify: `src/dotnet/Api/Constants.cs` (`MessageDataKeys`, ~lines 269-289)
- Modify: `src/dotnet/Notifications.Service/FirebaseMessagingClient.cs:88-99` (Android data block)
- Test: `tests/Notifications.IntegrationTests/PushMessageTest.cs` (new)

**Interfaces:**
- Consumes: `NotificationMessage` (Task 1), `ChatEntryRelatedNotification.RecentMessages` (Task 2).
- Produces: `PushMessage(string AuthorName, string Text, long SentAtMs)` with JSON names `n`/`t`/`ts`; `PushMessage.From(ApiArray<NotificationMessage>)` → `List<PushMessage>`; `PushMessage.ToJson(ApiArray<NotificationMessage>)` → `string` (drops oldest while JSON > `MaxJsonLength` = 2500); `PushMessage.FromJson(string?)` → `IReadOnlyList<PushMessage>` (empty on null/garbage, never throws); `Constants.Notification.MessageDataKeys.Messages` == `"messages"`. Task 5 calls exactly these from Android code.

- [ ] **Step 1: Write the failing tests**

Create `tests/Notifications.IntegrationTests/PushMessageTest.cs`:

```csharp
namespace ActualChat.Notifications.IntegrationTests;

public class PushMessageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void JsonRoundtrips()
    {
        var author = AuthorId.New(TestChatId, 1);
        var sentAt = Moment.Now;
        var messages = new[] {
            NotificationMessage.New(author, "Alice", "first", 100, sentAt),
            NotificationMessage.New(author, "Bob", "second", 101, sentAt + TimeSpan.FromSeconds(1)),
        }.ToApiArray();

        var parsed = PushMessage.FromJson(PushMessage.ToJson(messages));

        parsed.Should().HaveCount(2);
        parsed[0].AuthorName.Should().Be("Alice");
        parsed[0].Text.Should().Be("first");
        parsed[1].AuthorName.Should().Be("Bob");
        parsed[1].SentAtMs.Should().Be((long)(sentAt + TimeSpan.FromSeconds(1)).EpochOffset.TotalMilliseconds);
    }

    [Fact]
    public void ToJsonDropsOldestOverBudget()
    {
        var author = AuthorId.New(TestChatId, 1);
        // NotificationMessage.New truncates, so build oversized entries via the record directly.
        var messages = Enumerable.Range(0, 5)
            .Select(i => new NotificationMessage {
                AuthorId = author,
                AuthorName = $"author{i}",
                Text = new string((char)('a' + i), 700),
                EntryLid = 100 + i,
                SentAt = Moment.Now,
            })
            .ToApiArray();

        var json = PushMessage.ToJson(messages);

        json.Length.Should().BeLessThanOrEqualTo(PushMessage.MaxJsonLength + 800);
        var parsed = PushMessage.FromJson(json);
        parsed.Should().HaveCountLessThan(5);
        parsed[^1].Text.Should().StartWith("e"); // the newest message always survives
    }

    [Fact]
    public void FromJsonToleratesGarbage()
    {
        PushMessage.FromJson(null).Should().BeEmpty();
        PushMessage.FromJson("").Should().BeEmpty();
        PushMessage.FromJson("not json").Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~PushMessageTest" 2>&1 | tail -20`
Expected: build FAILURE — `PushMessage` does not exist.

- [ ] **Step 3: Create `PushMessage`**

Create `src/dotnet/Api/Notifications/PushMessage.cs`:

```csharp
namespace ActualChat.Notifications;

/// <summary>
/// JSON wire form of one <see cref="NotificationMessage"/> in the push payload's "messages" data
/// key (oldest -> newest); short property names keep the FCM message under its 4KB limit.
/// </summary>
public sealed record PushMessage(
    [property: JsonPropertyName("n")] string AuthorName,
    [property: JsonPropertyName("t")] string Text,
    [property: JsonPropertyName("ts")] long SentAtMs)
{
    public const int MaxJsonLength = 2500;

    public static List<PushMessage> From(ApiArray<NotificationMessage> messages)
        => messages
            .Select(m => new PushMessage(m.AuthorName, m.Text, (long)m.SentAt.EpochOffset.TotalMilliseconds))
            .ToList();

    public static string ToJson(ApiArray<NotificationMessage> messages)
    {
        // Drops oldest messages while the JSON exceeds the budget, so this key alone can't push
        // the whole FCM message over its 4KB limit.
        var items = From(messages);
        var json = JsonSerializer.Serialize(items);
        while (items.Count > 1 && json.Length > MaxJsonLength) {
            items.RemoveAt(0);
            json = JsonSerializer.Serialize(items);
        }
        return json;
    }

    public static IReadOnlyList<PushMessage> FromJson(string? json)
    {
        if (json.IsNullOrEmpty())
            return [];

        try {
            return JsonSerializer.Deserialize<List<PushMessage>>(json) ?? [];
        }
        catch (JsonException) {
            return [];
        }
    }
}
```

- [ ] **Step 4: Add the data key**

In `src/dotnet/Api/Constants.cs`, `MessageDataKeys`: add after `public const string ImageUrl = "imageUrl";`:

```csharp
            public const string Messages = "messages";
```

and add `Messages,` to the `ValidKeys` array (it's alphabetical-ish; insert after `Link,`):

```csharp
            public static readonly string[] ValidKeys = {
                Body, ChatId, ChatEntryId, DismissedIds, DismissedTags, LastEntryLocalId, Icon, ImageUrl, Kind, Link, Messages, NotificationId, Silent, Tag, Title, Timestamp
            };
```

- [ ] **Step 5: Write the key in `FirebaseMessagingClient`**

In `src/dotnet/Notifications.Service/FirebaseMessagingClient.cs`, `SendMessage`, replace the `Android = new AndroidConfig { ... }` initializer's `Data` property (lines 91–95):

```csharp
                Data = new Dictionary<string, string>() {
                    { Constants.Notification.MessageDataKeys.Title, title },
                    { Constants.Notification.MessageDataKeys.Body, content },
                    { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
                },
```

with a pre-built dictionary. Above `var multicastMessage = new MulticastMessage {` (line 83) add:

```csharp
        var androidData = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.Title, title },
            { Constants.Notification.MessageDataKeys.Body, content },
            { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
        };
        if (notification is ChatEntryRelatedNotification { RecentMessages.Count: > 0 } coalesced)
            androidData.Add(Constants.Notification.MessageDataKeys.Messages, PushMessage.ToJson(coalesced.RecentMessages));
```

and in the initializer use `Data = androidData,`.

- [ ] **Step 6: Run tests + build**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~PushMessageTest" 2>&1 | tail -20`
Expected: 3 passed.
Run: `dotnet build src/dotnet/Notifications.Service/Notifications.Service.csproj 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Notifications/PushMessage.cs src/dotnet/Api/Constants.cs src/dotnet/Notifications.Service/FirebaseMessagingClient.cs tests/Notifications.IntegrationTests/PushMessageTest.cs
git commit -m "feat(notifications): ship RecentMessages as a structured Android push payload"
```

---

### Task 5: Client rendering — Android `MessagingStyle` transcript + reconciler plumbing

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/IDeviceNotifications.cs:21-26` (`ActiveNotificationInfo`)
- Modify: `src/dotnet/UI.Blazor.App/Services/NotificationReconciler.cs:86-96` (`ToInfos`)
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationData.cs`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationHelper.cs:36-95`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs:181-185`
- Modify: `src/dotnet/App.Maui/Platforms/Android/Notifications/AndroidDeviceNotifications.cs:35-42`

**Interfaces:**
- Consumes: `PushMessage`, `PushMessage.From/FromJson`, `Constants.Notification.MessageDataKeys.Messages` (Task 4); `ChatEntryRelatedNotification.RecentMessages` (Task 2).
- Produces: `ActiveNotificationInfo` gains trailing `ApiArray<NotificationMessage> Messages = default`; `NotificationHelper.ShowChatNotification(string tag, string title, string body, string? imageUrl, string? link, bool silent = false, IReadOnlyList<PushMessage>? messages = null)`. No other platform impl changes (web/iOS ignore the new field).

- [ ] **Step 1: Extend `ActiveNotificationInfo` and the reconciler**

In `src/dotnet/UI.Blazor.App/Services/IDeviceNotifications.cs`, add at the top of the file:

```csharp
using ActualChat.Notifications;
```

and replace the record (lines 21–26) with:

```csharp
public sealed record ActiveNotificationInfo(
    string Tag,
    string Title,
    string Text,
    string IconUrl,
    string Url,
    ApiArray<NotificationMessage> Messages = default);
```

In `src/dotnet/UI.Blazor.App/Services/NotificationReconciler.cs`, `ToInfos` (lines 86–96), replace the `Select` projection with:

```csharp
            .Select(x => new ActiveNotificationInfo(
                x.Tag!,
                x.Notification.Title,
                x.Notification.Text,
                x.Notification.IconUrl,
                UrlMapper.ToAbsolute(x.Notification.GetChatLink()),
                (x.Notification as ChatEntryRelatedNotification)?.RecentMessages ?? default))
```

- [ ] **Step 2: Verify the buildable part compiles**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Parse the `messages` key on Android**

In `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationData.cs`, add after the `Tag` property:

```csharp
    // Structured transcript lines for MessagingStyle rendering; empty when the push predates them.
    public IReadOnlyList<PushMessage> Messages
        => PushMessage.FromJson(data.GetValueOrDefault(Constants.Notification.MessageDataKeys.Messages));
```

(`ActualChat.Notifications` types resolve here the same way `NotificationKind` already does.)

- [ ] **Step 4: Render the transcript in `NotificationHelper`**

In `src/dotnet/App.Maui/Platforms/Android/Notifications/NotificationHelper.cs`:

Add at the top of the file (with the other usings):

```csharp
using ActualChat.Notifications;
```

Replace `ShowChatNotification`'s signature (line 36):

```csharp
    public static void ShowChatNotification(
        string tag, string title, string body, string? imageUrl, string? link,
        bool silent = false, IReadOnlyList<PushMessage>? messages = null)
```

and its `CreateStyle` call (line 54):

```csharp
        builder.SetStyle(CreateStyle(title, body, largeImage, messages));
```

Replace `CreateStyle` (lines 60–87) with:

```csharp
    // Telegram-style rendering: one MessagingStyle line per pushed message (sender + text +
    // timestamp) when structured messages are present; single-message and BigTextStyle fallbacks
    // keep old-server pushes and non-chat titles working.
    private static NotificationCompat.Style CreateStyle(
        string title, string body, Bitmap? largeImage, IReadOnlyList<PushMessage>? messages)
    {
        var bigText = new NotificationCompat.BigTextStyle().BigText(body)!;
        var (senderName, conversationTitle) = SplitTitle(title);
        if (senderName.IsNullOrEmpty())
            return bigText;

        try {
            var self = new Person.Builder().SetName("You")!.Build();
            var style = new NotificationCompat.MessagingStyle(self);
            if (!conversationTitle.IsNullOrEmpty()) {
                style.SetGroupConversation(true);
                style.SetConversationTitle(conversationTitle);
            }
            if (messages is { Count: > 0 }) {
                // Only the newest sender carries the avatar — it's the banner headline's icon.
                var newestName = messages[^1].AuthorName.NullIfEmpty() ?? senderName;
                var persons = new Dictionary<string, Person>();
                foreach (var message in messages) {
                    var name = message.AuthorName.NullIfEmpty() ?? senderName;
                    if (!persons.TryGetValue(name, out var person)) {
                        var personBuilder = new Person.Builder().SetName(name)!;
                        if (name == newestName && largeImage != null)
                            personBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
                        person = personBuilder.Build();
                        persons.Add(name, person);
                    }
                    style.AddMessage(message.Text, message.SentAtMs, person);
                }
            }
            else {
                var senderBuilder = new Person.Builder().SetName(senderName)!;
                if (largeImage != null)
                    senderBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
                style.AddMessage(body, Java.Lang.JavaSystem.CurrentTimeMillis(), senderBuilder.Build());
            }
            return style;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to build MessagingStyle; falling back to BigTextStyle");
            return bigText;
        }
    }
```

- [ ] **Step 5: Wire up both callers**

In `src/dotnet/App.Maui/Platforms/Android/Notifications/FirebaseMessagingService.cs`, `ShowChatMessageNotification` (line 184), replace:

```csharp
        NotificationHelper.ShowChatNotification(data.Tag!, data.Title!, data.Body!, data.ImageUrl, data.Link, data.Silent);
```

with:

```csharp
        NotificationHelper.ShowChatNotification(
            data.Tag!, data.Title!, data.Body!, data.ImageUrl, data.Link, data.Silent, data.Messages);
```

In `src/dotnet/App.Maui/Platforms/Android/Notifications/AndroidDeviceNotifications.cs` (line 41), replace:

```csharp
                // Healing a dropped banner must not alert — it's a reconcile, not a new event.
                NotificationHelper.ShowChatNotification(info.Tag, info.Title, info.Text, info.IconUrl, info.Url, silent: true);
```

with:

```csharp
                // Healing a dropped banner must not alert — it's a reconcile, not a new event.
                NotificationHelper.ShowChatNotification(info.Tag, info.Title, info.Text, info.IconUrl, info.Url,
                    silent: true, messages: info.Messages.IsEmpty ? null : PushMessage.From(info.Messages));
```

(`AndroidDeviceNotifications.cs` needs `using ActualChat.Notifications;` if `PushMessage`/`NotificationMessage` don't already resolve — `ActualChat.UI.Blazor.App.Services` is imported, but the notification types live in `ActualChat.Notifications`; add the using.)

- [ ] **Step 6: Verify what's verifiable, flag the rest**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -5`
Expected: Build succeeded (App.Maui is excluded from the CI solution filter — the four Android files compile only on a machine with the MAUI Android workload).

Re-read the four Android diffs against the AndroidX API shapes already used in the file (every API in the new code — `Person.Builder`, `IconCompat.CreateWithBitmap`, `MessagingStyle.AddMessage(string, long, Person)` — appears in the pre-change code). Note in the final report that Android needs a host build + on-device check by the user.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/IDeviceNotifications.cs src/dotnet/UI.Blazor.App/Services/NotificationReconciler.cs src/dotnet/App.Maui/Platforms/Android/Notifications/
git commit -m "feat(notifications): Android MessagingStyle transcript from structured push messages"
```

---

### Task 6: Full verification pass

**Files:** none new — verification only.

- [ ] **Step 1: Full notification test suite**

Run: `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj --filter "FullyQualifiedName~NotificationAggregationTest|FullyQualifiedName~NotificationMessageTest|FullyQualifiedName~PushMessageTest" 2>&1 | tail -20`
Expected: all pass.

- [ ] **Step 2: Full CI build**

Run: `dotnet build ActualChat.CI.slnf 2>&1 | tail -5`
Expected: Build succeeded, no new warnings in changed projects.

- [ ] **Step 3: Grep for leftovers**

Run: `rg -n "LeadRollInThreshold|GetAggregatedText" src/ tests/`
Expected: no matches (both were fully replaced).

- [ ] **Step 4: Report**

Summarize for the user:
- what changed (newest-first body everywhere; Android transcript),
- what is verified (unit tests, CI build),
- what needs manual verification: build `App.Maui` for Android on the host and check on a device — transcript lines with senders, collapsed view shows the newest message, silent same-tag update doesn't re-alert, old-push fallback still renders.
