# Deferred Conversation Collapse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A conversation summary block must not collapse ("swallow") entries the user is currently looking at; it collapses on the next chat visit instead.

**Architecture:** Client-only change inside `ChatUI` (Blazor UI service). Each `GetChatItems` build accumulates the lids the user actually saw rendered as plain entry rows (`ChatDataQuery.VisibleLidRange` ∩ built entry rows) into a per-chat "witnessed" range set. A conversation that is effective-collapsed, not manually toggled, and intersecting witnessed lids gets auto-expanded via a new reactive set that unions into `ConversationViewState.ExpandedConversations`. All per-visit state clears on chat switch. Spec: `docs/superpowers/specs/2026-08-31-deferred-conversation-collapse-design.md`.

**Tech Stack:** C# / Blazor, ActualLab.Fusion (`MutableState`, compute methods), xUnit + AwesomeAssertions.

**Build environment (Docker container):** the repo pins .NET `11.0.100-preview.7.26381.103`; if `dotnet --version` fails on `global.json`, install it first:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --version 11.0.100-preview.7.26381.103 --install-dir $HOME/.dotnet
export DOTNET_ROOT=$HOME/.dotnet && export PATH=$HOME/.dotnet:$PATH
dotnet workload install wasm-tools
```

Prefix every `dotnet` command below with `export DOTNET_ROOT=$HOME/.dotnet && export PATH=$HOME/.dotnet:$PATH &&` (shell state does not persist between calls). First build of a project needs `dotnet restore <csproj>` once.

**Key existing code:**

- `src/dotnet/UI.Blazor.App/Services/ChatUI.cs` — fields/ctor (lines ~19–101), `ToggleExpandConversation` (~450), `IsConversationExpanded` (~468), `EnsureConversationCollapsed` (~471), `SelectChatInternal` (~564).
- `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` — `GetChatItemsInternal`: `chatRangeMetaList` (~418), `showConversations` expansion block (~437–503), `groupedItems` assembly (~699), `GroupExpandedConversations` call (~759).
- `src/dotnet/UI.Blazor.App/Services/ChatDataQuery.cs` — `VisibleLidRange` (currently-visible entry lid range, flows into every build).
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessage.cs` — the "plain entry row" message type; collapsed conversations never emit these (their id-tiles are excluded from loading), so filtering leaves to `ChatEntryMessage` automatically satisfies spec test "collapsed block's covered lids are not witnessed".
- Tests to mirror: `tests/Chat.UI.Blazor.UnitTests/GroupExpandedConversationsTest.cs` (statics are `InternalsVisibleTo`-accessible), `tests/Chat.UI.Blazor.IntegrationTests/ConversationCollapseTest.cs` (drives real `ChatUI.GetChatItems`).

**Style:** read `docs/CODING_STYLE.md` before writing code. Notable: no `Async` suffix, K&R braces except types/methods, control-flow statements on own line + blank line after, tests use `Should` naming + lowercase `// arrange` / `// act` / `// assert`, almost no comments.

---

### Task 1: `LidRangeSet`

Thread-safe accumulator of merged lid ranges with intersection test.

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Services/LidRangeSet.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/LidRangeSetTest.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ActualChat.UI.Blazor.UnitTests;

public class LidRangeSetTest
{
    [Fact]
    public void ShouldMergeAdjacentAndOverlappingRanges()
    {
        // arrange
        var set = new LidRangeSet();

        // act
        set.Add(new Range<long>(10, 12));
        set.Add(new Range<long>(12, 15));
        set.Add(new Range<long>(11, 13));
        set.Add(new Range<long>(20, 21));

        // assert
        set.Intersects(new Range<long>(10, 15)).Should().BeTrue();
        set.Intersects(new Range<long>(14, 16)).Should().BeTrue();
        set.Intersects(new Range<long>(15, 20)).Should().BeFalse();
        set.Intersects(new Range<long>(20, 25)).Should().BeTrue();
        set.Intersects(new Range<long>(25, 30)).Should().BeFalse();
    }

    [Fact]
    public void ShouldIgnoreEmptyRanges()
    {
        // arrange
        var set = new LidRangeSet();

        // act
        set.Add(default);
        set.Add(new Range<long>(5, 5));

        // assert
        set.Intersects(new Range<long>(0, 100)).Should().BeFalse();
    }

    [Fact]
    public void ShouldNotIntersectWithEmptyProbe()
    {
        // arrange
        var set = new LidRangeSet();
        set.Add(new Range<long>(10, 20));

        // act + assert
        set.Intersects(default).Should().BeFalse();
        set.Intersects(new Range<long>(15, 15)).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --no-restore`
Expected: compile error — `LidRangeSet` not defined.

- [ ] **Step 3: Write the implementation**

`src/dotnet/UI.Blazor.App/Services/LidRangeSet.cs`:

```csharp
namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Thread-safe accumulator of half-open <c>[Start, End)</c> lid ranges, merged on insert.
/// </summary>
internal sealed class LidRangeSet
{
    private readonly object _lock = new();
    private readonly List<Range<long>> _ranges = new();

    public void Add(Range<long> range)
    {
        if (range.IsEmptyOrNegative)
            return;

        lock (_lock) {
            var index = _ranges.FindIndex(r => r.End >= range.Start);
            if (index < 0) {
                _ranges.Add(range);
                return;
            }

            if (_ranges[index].Start > range.End) {
                _ranges.Insert(index, range);
                return;
            }

            // Overlaps or touches _ranges[index]; absorb every subsequent range it reaches
            var merged = _ranges[index].MinMaxWith(range);
            var end = index + 1;
            while (end < _ranges.Count && _ranges[end].Start <= merged.End) {
                merged = merged.MinMaxWith(_ranges[end]);
                end++;
            }
            _ranges[index] = merged;
            _ranges.RemoveRange(index + 1, end - index - 1);
        }
    }

    public bool Intersects(Range<long> range)
    {
        if (range.IsEmptyOrNegative)
            return false;

        lock (_lock)
            return _ranges.Any(r => r.Start < range.End && range.Start < r.End);
    }
}
```

Note: `IsEmptyOrNegative` is a property on `Range<T>`; `MinMaxWith` comes from `RangeExt` (used for `Range<long>` at `ChatUI.Tiles.cs:490`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~LidRangeSetTest"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/LidRangeSet.cs tests/Chat.UI.Blazor.UnitTests/LidRangeSetTest.cs
git commit -m "feat(ui): add LidRangeSet for witnessed-lid tracking"
```

---

### Task 2: Auto-expansion state and decision rule in `ChatUI`

State fields, chat-switch clearing, the pure decision function, and the toggle changes.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.cs`
- Test: `tests/Chat.UI.Blazor.UnitTests/AutoExpansionRuleTest.cs`

- [ ] **Step 1: Write the failing tests for the decision rule**

```csharp
using ActualChat.Chat;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class AutoExpansionRuleTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly IImmutableSet<ConversationId> Empty = ImmutableHashSet<ConversationId>.Empty;

    [Fact]
    public void ShouldAutoExpandCollapsedConversationOverWitnessedLids()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var range = new Range<long>(95, 120);

        // act
        var result = ChatUI.GetNewAutoExpansions(
            TestChatId, [range], Empty, Empty, Empty, _ => false, witnessed, null, null);

        // assert
        result.Should().Equal(ConversationId.New(TestChatId, 95));
    }

    [Fact]
    public void ShouldSkipExpandedSuppressedAndNonWitnessedConversations()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var witnessedRange = new Range<long>(95, 120);          // witnessed, but variants below block it
        var farRange = new Range<long>(500, 600);               // never witnessed
        var witnessedId = ConversationId.New(TestChatId, 95);

        // act + assert: default-expanded
        ChatUI.GetNewAutoExpansions(
                TestChatId, [witnessedRange, farRange],
                ImmutableHashSet.Create(witnessedId), Empty, Empty, _ => false, witnessed, null, null)
            .Should().BeEmpty();
        // act + assert: expanded via override (default-collapsed XOR override = expanded)
        ChatUI.GetNewAutoExpansions(
                TestChatId, [witnessedRange, farRange],
                Empty, ImmutableHashSet.Create(witnessedId), Empty, _ => false, witnessed, null, null)
            .Should().BeEmpty();
        // act + assert: suppressed by a manual toggle
        ChatUI.GetNewAutoExpansions(
                TestChatId, [witnessedRange, farRange],
                Empty, Empty, Empty, id => id == witnessedId, witnessed, null, null)
            .Should().BeEmpty();
        // act + assert: already auto-expanded - no duplicate
        ChatUI.GetNewAutoExpansions(
                TestChatId, [witnessedRange, farRange],
                Empty, Empty, ImmutableHashSet.Create(witnessedId), _ => false, witnessed, null, null)
            .Should().BeEmpty();
    }

    [Fact]
    public void ShouldSkipLiveAndMaterializedBlockIds()
    {
        // arrange
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var range = new Range<long>(95, 120);
        var id = ConversationId.New(TestChatId, 95);

        // act + assert
        ChatUI.GetNewAutoExpansions(
                TestChatId, [range], Empty, Empty, Empty, _ => false, witnessed, id, null)
            .Should().BeEmpty();
        ChatUI.GetNewAutoExpansions(
                TestChatId, [range], Empty, Empty, Empty, _ => false, witnessed, null, id)
            .Should().BeEmpty();
    }

    [Fact]
    public void ShouldReTriggerWhenRangeGrowsOverWitnessedLids()
    {
        // arrange: the conversation used to end before the witnessed rows, now covers them
        var witnessed = new LidRangeSet();
        witnessed.Add(new Range<long>(100, 110));
        var grownRange = new Range<long>(50, 105);

        // act
        var result = ChatUI.GetNewAutoExpansions(
            TestChatId, [grownRange], Empty, Empty, Empty, _ => false, witnessed, null, null);

        // assert
        result.Should().Equal(ConversationId.New(TestChatId, 50));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --no-restore`
Expected: compile error — `GetNewAutoExpansions` not defined.

- [ ] **Step 3: Add state fields, property, and constructor init to `ChatUI.cs`**

After the `_conversationExpansionOverrides` field (line ~28):

```csharp
    private readonly MutableState<IImmutableSet<ConversationId>> _autoExpandedConversations;
    private readonly ConcurrentDictionary<ChatId, LidRangeSet> _witnessedLids = new();
    private readonly ConcurrentDictionary<ConversationId, Unit> _autoExpansionSuppressed = new();
```

After the `ConversationExpansionOverrides` property (line ~61):

```csharp
    public IState<IImmutableSet<ConversationId>> AutoExpandedConversations => _autoExpandedConversations;
```

In the constructor, right after the `_conversationExpansionOverrides` init (line ~93):

```csharp
        _autoExpandedConversations = StateFactory.NewMutable(
            (IImmutableSet<ConversationId>)ImmutableHashSet<ConversationId>.Empty,
            StateCategories.Get(type, nameof(AutoExpandedConversations)));
```

- [ ] **Step 4: Add the decision rule and the chat-switch clearing**

In `ChatUI.cs`, next to `GroupExpandedConversations`-style internals (a good spot: below `EnsureConversationCollapsed`):

```csharp
    // A conversation that appeared (or grew) over rows the user has actually seen this visit must not
    // swallow them in place; it auto-expands until the user leaves the chat. Live/materialized block
    // ids are excluded - the live overlay machinery owns their expansion.
    internal static List<ConversationId> GetNewAutoExpansions(
        ChatId chatId,
        IEnumerable<Range<long>> conversationLidRanges,
        IImmutableSet<ConversationId> defaultExpanded,
        IImmutableSet<ConversationId> overrides,
        IImmutableSet<ConversationId> autoExpanded,
        Func<ConversationId, bool> isSuppressed,
        LidRangeSet witnessedLids,
        ConversationId? liveBlockId,
        ConversationId? materializedBlockId)
    {
        var result = new List<ConversationId>();
        foreach (var range in conversationLidRanges) {
            var conversationId = ConversationId.New(chatId, range.Start);
            if (conversationId == liveBlockId || conversationId == materializedBlockId)
                continue;
            if (autoExpanded.Contains(conversationId) || isSuppressed(conversationId))
                continue;

            var isExpanded = defaultExpanded.Contains(conversationId) ^ overrides.Contains(conversationId);
            if (isExpanded)
                continue;

            if (witnessedLids.Intersects(range))
                result.Add(conversationId);
        }
        return result;
    }

    private void ClearAutoExpansionState(ChatId? keepChatId)
    {
        foreach (var chatId in _witnessedLids.Keys)
            if (chatId != keepChatId)
                _witnessedLids.TryRemove(chatId, out _);
        foreach (var conversationId in _autoExpansionSuppressed.Keys)
            if (conversationId.ChatId != keepChatId)
                _autoExpansionSuppressed.TryRemove(conversationId, out _);
        var autoExpanded = _autoExpandedConversations.Value;
        var kept = autoExpanded.Where(id => id.ChatId == keepChatId).ToImmutableHashSet();
        if (kept.Count != autoExpanded.Count)
            _autoExpandedConversations.Value = kept;
    }
```

In `SelectChatInternal` (line ~564), after `selectedChatId.Value = chatId;` and before `return true;`:

```csharp
            ClearAutoExpansionState(chatId);
```

- [ ] **Step 5: Update `ToggleExpandConversation`, `IsConversationExpanded`, `EnsureConversationCollapsed`**

`ToggleExpandConversation` — after the `TryCollapseOverlay` early-return, before the override flip:

```csharp
        _autoExpansionSuppressed[conversationId] = default;
        var autoExpanded = _autoExpandedConversations.Value;
        if (autoExpanded.Contains(conversationId)) {
            // The conversation renders expanded only via auto-expansion (its XOR state is collapsed),
            // so removing it from the auto set IS the collapse - flipping the override would expand it.
            _autoExpandedConversations.Value = autoExpanded.Remove(conversationId);
            Hub.LiveBlockUI.ResetReveal(conversationId.ChatId);
            return;
        }
```

`IsConversationExpanded`:

```csharp
    public bool IsConversationExpanded(Conversation conversation)
        => (conversation.IsExpandedByDefault ^ _conversationExpansionOverrides.Value.Contains(conversation.Id))
            || _autoExpandedConversations.Value.Contains(conversation.Id);
```

`EnsureConversationCollapsed` (called from `LiveBlockUI.TryCollapseOverlay` — an explicit "collapse the block" gesture, so it must both undo and suppress auto-expansion):

```csharp
    internal void EnsureConversationCollapsed(ConversationId conversationId, bool isExpandedByDefault)
    {
        _autoExpansionSuppressed[conversationId] = default;
        _autoExpandedConversations.Value = _autoExpandedConversations.Value.Remove(conversationId);
        var overrides = _conversationExpansionOverrides.Value;
        _conversationExpansionOverrides.Value = isExpandedByDefault
            ? overrides.Add(conversationId)
            : overrides.Remove(conversationId);
    }
```

- [ ] **Step 6: Run the new tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~AutoExpansionRuleTest"`
Expected: 4 passed.

- [ ] **Step 7: Build the app project, run the full unit-test project**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj --no-restore`
Expected: 0 errors.
Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatUI.cs tests/Chat.UI.Blazor.UnitTests/AutoExpansionRuleTest.cs
git commit -m "feat(ui): auto-expansion state and rule for deferred conversation collapse"
```

---

### Task 3: Wire the rule and witness capture into `GetChatItemsInternal`

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs`

- [ ] **Step 1: Apply the auto-expansion rule when resolving `expandedConversations`**

In the `showConversations` block, locate (line ~496):

```csharp
            expandedConversations = defaultExpanded.SymmetricExcept(overrides);
```

Replace with:

```csharp
            var autoExpanded = await AutoExpandedConversations.Use(cancellationToken).ConfigureAwait(false);
            if (!isPrefetch && _witnessedLids.TryGetValue(chatId, out var witnessedLids)) {
                var newAutoExpansions = GetNewAutoExpansions(
                    chatId,
                    chatRangeMetaList.SelectMany(m => m.ConversationLidRanges),
                    defaultExpanded,
                    overrides,
                    autoExpanded,
                    id => _autoExpansionSuppressed.ContainsKey(id),
                    witnessedLids,
                    liveConversation?.Id,
                    materializedBlockId);
                if (newAutoExpansions.Count > 0) {
                    autoExpanded = autoExpanded.Union(newAutoExpansions);
                    _autoExpandedConversations.Value = autoExpanded;
                }
            }
            expandedConversations = defaultExpanded.SymmetricExcept(overrides).Union(autoExpanded);
```

Notes for the implementer:
- `chatRangeMetaList`, `liveConversation`, and `materializedBlockId` are already in scope at this point (range meta is resolved at line ~418, the live conversation/blocks earlier). If the local for the materialized block id has a different name at this line, use the variable that feeds `GroupExpandedConversations`'s `materializedBlockId` argument (line ~760).
- The state write is guarded by `newAutoExpansions.Count > 0`, so repeated builds converge without invalidation loops (mirrors the navigation-expansion write at line ~460).

- [ ] **Step 2: Capture witnessed lids after the items are built**

After `var groupedItems = GroupAuthorMessages(items);` (line ~699):

```csharp
        if (!isPrefetch && !dataQuery.VisibleLidRange.IsEmpty) {
            var visibleLidRange = dataQuery.VisibleLidRange;
            var witnessed = _witnessedLids.GetOrAdd(chatId, static _ => new LidRangeSet());
            foreach (var message in groupedItems.SelectMany(i => i.GetLeafMessages()))
                if (message is ChatEntryMessage
                    && message.Id >= visibleLidRange.Start
                    && message.Id <= visibleLidRange.End)
                    witnessed.Add(new Range<long>(message.Id, message.Id + 1));
        }
```

(`message.Id <= visibleLidRange.End` is deliberately inclusive of `End` — over-witnessing one borderline entry is harmless and inclusive/exclusive conventions for `VisibleLidRange` differ by caller.)

- [ ] **Step 3: Build**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj --no-restore`
Expected: 0 errors. Fix name mismatches (e.g. the actual local names for the live/materialized block ids at the insertion point) by reading the surrounding code, not by renaming existing locals.

- [ ] **Step 4: Run existing UI unit + collapse integration tests (regression)**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj`
Expected: all pass.
Run: `dotnet restore tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj && dotnet build tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-restore && dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ConversationCollapseTest|FullyQualifiedName~LiveConversationDisplayTest"`
Expected: all pass (host infra — PostgreSQL/Redis/NATS — is already running per CLAUDE.md).

- [ ] **Step 5: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs
git commit -m "feat(ui): defer conversation collapse for entries on screen"
```

---

### Task 4: End-to-end integration tests

**Files:**
- Modify: `tests/Chat.UI.Blazor.IntegrationTests/ConversationCollapseTest.cs`

- [ ] **Step 1: Parametrize the existing `Materialize` helper**

Change its signature to accept the default-expansion flag (single call site in this file passes nothing new):

```csharp
    private async Task<ConversationId> Materialize(
        ChatId chatId, ChatEntry first, ChatEntry last, bool isExpandedByDefault = true)
    {
        var id = ConversationId.New(chatId, first.LocalId);
        var conversation = new Conversation(id) {
            Title = $"Recap {first.LocalId}",
            Summary = "s",
            Description = "d",
            EndEntryLid = last.LocalId,
            MessageCount = (int)(last.LocalId - first.LocalId + 1),
            IsExpandedByDefault = isExpandedByDefault,
        };
        await Tester.Commander.Call(new ConversationBackend_Materialize(conversation), CancellationToken.None);
        return id;
    }
```

- [ ] **Step 2: Write the deferred-collapse test (failing only if Task 3 is broken — this validates end-to-end)**

Add to `ConversationCollapseTest`:

```csharp
    [Fact]
    public async Task ShouldKeepWitnessedEntriesExpandedWhenConversationMaterializes()
    {
        // arrange: the user is looking at the entries (VisibleLidRange covers them) before any
        // conversation exists over them
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse");
        var (otherChat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse-other");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 20; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);
        var query = new ChatDataQuery(
            new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit) {
            VisibleLidRange = new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
        };
        var before = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        before.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == entries[5].LocalId, "the entries are on screen pre-materialization");

        // act: a collapsed-by-default conversation materializes over the witnessed entries
        var conversationId = await Materialize(chat.Id, entries[0], entries[9], isExpandedByDefault: false);
        var after = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: the entries stay rendered - the conversation is auto-expanded, not swallowed
        after.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == entries[5].LocalId, "witnessed entries must not collapse in place");

        // act 2: leave the chat and come back
        chatUI.SelectChatOnNavigation(otherChat.Id);
        chatUI.SelectChatOnNavigation(chat.Id);
        var fresh = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert 2: the conversation now renders collapsed - its entries are gone, its card is there
        fresh.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "a fresh visit renders per tier");
        fresh.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == conversationId.StartEntryLid);
    }

    [Fact]
    public async Task ShouldKeepManualCollapseOfAutoExpandedConversation()
    {
        // arrange: same witnessed setup, conversation auto-expands
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "deferred-collapse-manual");
        var entries = new List<ChatEntry>();
        for (var i = 0; i < 20; i++)
            entries.Add(await Tester.CreateTextEntry(chat.Id, $"m-{i}"));
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        chatUI.SelectChatOnNavigation(chat.Id);
        var query = new ChatDataQuery(
            new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
            -chatUI.HalfLoadLimit,
            chatUI.HalfLoadLimit) {
            VisibleLidRange = new Range<long>(entries[0].LocalId, entries[^1].LocalId + 1),
        };
        await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        var conversationId = await Materialize(chat.Id, entries[0], entries[9], isExpandedByDefault: false);
        var autoExpanded = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        autoExpanded.Items.SelectMany(i => i.GetLeafMessages())
            .Should().Contain(m => m.Id == entries[5].LocalId);

        // act: the user collapses it by hand
        chatUI.ToggleExpandConversation(conversationId);
        var collapsed = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert: collapsed, and it stays collapsed on the next rebuild (no auto re-add)
        collapsed.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "a manual collapse must win over auto-expansion");
        var rebuilt = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);
        rebuilt.Items.SelectMany(i => i.GetLeafMessages())
            .Should().NotContain(m => m.Id == entries[5].LocalId, "suppression must survive rebuilds");
    }
```

Implementer notes:
- If `Tester.CreateAndGetChat` / `Tester.CreateTextEntry` names differ, mirror the exact helpers the existing test in this file uses.
- `GetChatItems`'s third argument mirrors the existing test (`shownReadyEntryLid = 0`).
- If the first `GetChatItems` build doesn't yet reflect the just-materialized conversation (compute invalidation raciness), wrap the post-materialization read in `ComputedTest.When(..., TimeSpan.FromSeconds(10))` — see `tests/Chat.IntegrationTests/CallNotificationFlowTest.cs` for the pattern.

- [ ] **Step 3: Run the new tests**

Run: `dotnet build tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-restore && dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ShouldKeepWitnessedEntriesExpandedWhenConversationMaterializes|FullyQualifiedName~ShouldKeepManualCollapseOfAutoExpandedConversation"`
Expected: 2 passed. Run the filter 3 times in a row to check for flakiness before proceeding.

- [ ] **Step 4: Commit**

```bash
git add tests/Chat.UI.Blazor.IntegrationTests/ConversationCollapseTest.cs
git commit -m "test(ui): deferred conversation collapse end-to-end coverage"
```

---

### Task 5: Full regression pass

- [ ] **Step 1: Run the affected test suites**

```bash
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ConversationCollapseTest|FullyQualifiedName~LiveConversationDisplayTest|FullyQualifiedName~ChatUICacheTest|FullyQualifiedName~SendingMessagesDisplayTest"
```

Expected: all pass. Known pre-existing failure unrelated to this work: `ChatEntryReaderTest.FindByMinBeginsAtTest` (in `Chat.IntegrationTests`, fails on clean HEAD too — ignore it, don't "fix" it here).

- [ ] **Step 2: Verify no stray changes and commit anything uncommitted**

Run: `git status --short`
Expected: clean (every change was committed in Tasks 1–4).
