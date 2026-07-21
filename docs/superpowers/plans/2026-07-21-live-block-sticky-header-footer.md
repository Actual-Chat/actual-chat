# Live Block — Sticky Header, Scrollable Body, Footer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the joined/expanded live conversation block behave and read like a regular expanded conversation — a short sticky title header, a scrollable summary/description, sticky author badges, and a closed box with a live-styled footer — without VirtualList jump regressions.

**Architecture:** The joined-live block already renders its entries with the same author-group markup as regular conversations; sticky is only broken by an `overflow: hidden` on the group wrapper (added to clip the tint). We (1) give the block a footer so it's a bounded box and the tint becomes the block background, letting us drop `overflow: hidden` — which restores sticky author badges — and (2) split the monolithic live card into a short **sticky title-band item** (rendered like `ConversationHeader`) and a **scrollable description item**, so a recent joiner's summary scrolls out while the title stays pinned. Client/UI only — no server, DB, protocol, or Fusion changes.

**Tech Stack:** C# / Blazor (Razor components), Tailwind-flavored CSS (`@apply`), ActualLab.Fusion compute state, xUnit integration tests (`ChatAppHostFixture` / `BlazorTester`), chrome-devtools MCP + `/virtual-list-debug` for the browser pass.

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing C#/TS. No `Async` suffix on async methods; no XML docs on members; comments only for non-obvious constraints (no restating code).
- Build with `dotnet build ActualChat.CI.slnf` (not the full `.sln`).
- TypeScript/CSS changes: validate with `npm run build:Verify` (tsc + eslint + debug build). No `.ts` changes are expected here, but CSS is touched — still run it.
- Never `/server-start` or `/server-restart`; the user's `dotnet watch` owns the server on port 7080. After edits, poll `tmp/watch-dotnet.log` for `Now listening on:` or `error`.
- Never push; commit only. Branch is `feat/live-block-ux-polish` (already checked out).
- Reuse first: the sticky mechanism is existing CSS (`chat-view.css:717-747`, `virtual-list.css:143`) — do NOT write new sticky JS or duplicate the badge rules. New pieces are a footer item/view, a description item/view, and the title-band split of the existing card view.

---

### Task 1: Live footer closes the box (restores sticky author badges)

Give the joined-live block a footer as its last child, turn the tint into the block's own background, and drop the `overflow: hidden` that was clipping the sticky badges. This task alone makes author badges sticky again; Task 2 adds the sticky title band on top.

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationFooter.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationFooterView.razor`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/live-conversation-footer.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor` (dispatch the new item)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` (`GroupExpandedConversations.FinalizeBlock`, ~939)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css:315-347` (tint/overflow)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Produces: `LiveConversationFooter : ChatMessage` — item type marking the live block's bottom edge; ctor `LiveConversationFooter(Conversation conversation)`; exposes `Conversation`. Rendered by `LiveConversationFooterView`.
- Consumes: `ExpandedConversationMessage`, `ConversationMessage`, `GroupExpandedConversations(messages, liveBlockId, liveBlockRange)` (existing, `ChatUI.Tiles.cs:893`).

- [ ] **Step 1: Write the failing test**

Add to `LiveConversationDisplayTest.cs` (mirror the joined-block setup from `LeaveKeepsRenderedTailVisible`):

```csharp
[Fact]
public async Task JoinedLiveBlockEmitsSingleLiveFooterAsLastChild()
{
    // A joined live block must close with exactly one live footer as its last child - the box the
    // tint fills and the sticky containing block bound. The regular ConversationFooter is never
    // emitted for the live block.

    // arrange
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "live-footer-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_180);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    live.Should().NotBeNull();
    var v = live!.EffectiveVisibleStartLid;
    for (var i = 0; i < 3; i++)
        await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
    await liveBackend.UpdateSummary(chat.Id,
        new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s",
            EndEntryLid = v + 2, MessageCount = 3,
        }, CancellationToken.None);
    for (var i = 0; i < 3; i++)
        await Tester.CreateTextEntry(chat.Id, $"tail-{i}");

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    await chatAudioUI.SetListeningState(chat.Id, true);
    chatUI.SelectChatOnNavigation(chat.Id);
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

    // act + assert
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        var block = items.Items.OfType<ExpandedConversationMessage>().Single();
        block.Items.OfType<LiveConversationFooter>().Should().ContainSingle();
        block.Items[^1].Should().BeOfType<LiveConversationFooter>();
        items.Items.SelectMany(i => i.GetLeafMessages())
            .OfType<ConversationFooter>().Should().BeEmpty();
    }, TimeSpan.FromSeconds(10));
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~JoinedLiveBlockEmitsSingleLiveFooterAsLastChild" -v minimal`
Expected: FAIL — `LiveConversationFooter` does not exist (compile error), then once created, `ContainSingle()` fails (no footer emitted).

- [ ] **Step 3: Create the `LiveConversationFooter` item**

`LiveConversationFooter.cs` (mirror `ConversationFooter.cs`, keyed off `EndEntryLid` but tagged as the live tail bottom):

```csharp
namespace ActualChat.UI.Blazor.App.Components;

public sealed class LiveConversationFooter : ChatMessage
{
    public LiveConversationFooter(Conversation conversation) : base(conversation.EndEntryLid)
    {
        Conversation = conversation;
        ShouldSkipKey = true;
    }

    public override bool Equals(ChatMessage? other)
        => ReferenceEquals(this, other)
            || (other is LiveConversationFooter o
                && Conversation!.VersionEquals(o.Conversation)
                && Kind == o.Kind && Date == o.Date && Flags == o.Flags);

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Kind, Date, Flags);
}
```

- [ ] **Step 4: Emit the footer as the block's last child**

In `ChatUI.Tiles.cs`, `GroupExpandedConversations.FinalizeBlock` (~939). `liveBlockId` is a parameter of `GroupExpandedConversations` and is in scope in the local function:

```csharp
void FinalizeBlock()
{
    if (blockConversation == null)
        return;

    if (blockConversation.Id == liveBlockId)
        blockItems.Add(new LiveConversationFooter(blockConversation) {
            Kind = ChatMessageKind.ConversationEnd,
            PreviousMessage = blockItems.Count > 0 ? blockItems[^1] : null,
        });
    result.Add(new ExpandedConversationMessage(blockConversation, blockItems));
    blockConversation = null;
    blockItems = [];
}
```

- [ ] **Step 5: Render the footer**

`LiveConversationFooterView.razor` (minimal live band — no authors/count/"ended at"; a rounded bottom band with a subtle live indicator):

```razor
@namespace ActualChat.UI.Blazor.App.Components

<div class="live-conversation-footer">
    <chat-activity-panel-icon-svg size="4" isActive="true" mode="audio"/>
    <span class="c-live-label">Live</span>
</div>

@code {
    [Parameter, EditorRequired] public ChatContext ChatContext { get; set; } = null!;
    [Parameter, EditorRequired] public LiveConversationFooter Footer { get; set; } = null!;
}
```

`live-conversation-footer.css`:

```css
.live-conversation-footer {
    @apply flex items-center gap-x-2;
    @apply px-4 py-1.5;
    @apply text-sm text-02;
    @apply rounded-b-2xl;
}
.live-conversation-footer .c-live-label {
    @apply text-cr-primary;
}
```

Dispatch it in `ChatView.razor` `RenderChatMessage`, next to the `ConversationFooter` branch:

```razor
else if (item is LiveConversationFooter liveConversationFooter)
{
    <LiveConversationFooterView ChatContext="@chatContext" Footer="@liveConversationFooter" />
}
```

- [ ] **Step 6: Turn the tint into the block background and drop the clip**

In `conversation.css`, replace the joined-group block (`:315-347`, the `overflow-hidden` + fixed-height `::before` tint) so the tint is the block's own background bounded top-to-footer, and no ancestor clips the badges:

```css
/* Joined live block: the card is the sticky header, the entries carry sticky author badges, and a
   live footer closes the box. The tint is the block's own background (bounded by the footer's rounded
   bottom), so nothing needs overflow:hidden - which is what lets position:sticky badges work. */
.virtual-list .c-virtual-container .group:has(> .item > .conversation-message.live.joined),
.virtual-list .c-virtual-container .group:has(> .item.expanded > .live-conversation-header) {
    isolation: isolate;
    @apply rounded-2xl;
    background: linear-gradient(to bottom, rgba(166, 53, 255, 0.10), rgba(166, 53, 255, 0.03));
}
```

(Delete the old `.group:has(...live.joined) > :nth-child(2)::before` tint rules at `:329-347`.)

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~JoinedLiveBlockEmitsSingleLiveFooterAsLastChild" -v minimal`
Expected: PASS. Then run the whole file to confirm no regression:
`dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~LiveConversationDisplayTest" -v minimal` — expect all green (13 tests).

- [ ] **Step 8: Build CSS + commit**

Run: `npm run build:Verify` (expect clean). Then:

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationFooter.cs \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationFooterView.razor \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/live-conversation-footer.css \
        src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor \
        src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css \
        tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): close the joined live block with a footer so sticky badges work

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Split the live card into a sticky title band + scrollable description

Turn the monolithic `.c-live-card` into two block items: a short **sticky title band** (rendered like `ConversationHeader`, so the VirtualList wraps it in `.item expanded` = sticky) and a **scrollable description** (summary + meta + controls + tail preview). The unjoined standalone preview card is unchanged.

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeader.cs`
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor` (dispatch header item; wrap it as sticky `.item expanded`)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor` (render only the scrollable description when part of the live block; keep the full compact card for the unjoined standalone case)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` (`EmitConversationCard`, ~802 — emit the header item before the card for the live block)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css` (title-band sticky + description styles)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Produces: `LiveConversationHeader : ChatMessage` — the sticky title band; ctor `LiveConversationHeader(Conversation conversation)`; exposes `Conversation`. Rendered by `LiveConversationHeaderView`. In `ChatView.razor`'s `ExpandedConversationMessage` loop it is wrapped in `<div class="item expanded" data-skip="true">` (the same sticky wrapper as `ConversationHeader`).
- Consumes: `EmitConversationCard` (existing local fn), the live-block card `ConversationMessage`, `LiveBlockUI.GetBlockState` / `LiveSessionUI` state (existing, for title/fold/participant data already computed in `ConversationMessageView.ComputeState`).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task JoinedLiveBlockSplitsIntoStickyHeaderAndDescription()
{
    // The live card splits into a sticky title-band item (rendered like ConversationHeader) plus a
    // scrollable description item - so a joiner's summary scrolls out while the title stays pinned.

    // arrange - same joined-block setup with a landed title
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "live-split-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_190);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    for (var i = 0; i < 3; i++)
        await Tester.CreateTextEntry(chat.Id, $"folded-{i}");
    await liveBackend.UpdateSummary(chat.Id,
        new LiveSessionSummary {
            Title = "Recap", Description = "d", Summary = "s",
            EndEntryLid = v + 2, MessageCount = 3,
        }, CancellationToken.None);

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    await chatAudioUI.SetListeningState(chat.Id, true);
    chatUI.SelectChatOnNavigation(chat.Id);
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

    // assert - the block leads with a header item then the description card, in that order
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        var block = items.Items.OfType<ExpandedConversationMessage>().Single();
        block.Items.OfType<LiveConversationHeader>().Should().ContainSingle();
        var headerIndex = block.Items.FindIndex(i => i is LiveConversationHeader);
        var cardIndex = block.Items.FindIndex(i => i is ConversationMessage);
        headerIndex.Should().BeGreaterThanOrEqualTo(0);
        cardIndex.Should().BeGreaterThan(headerIndex, "the scrollable description card follows the sticky header");
    }, TimeSpan.FromSeconds(10));
}
```

(If `block.Items` has no `FindIndex`, materialize with `block.Items.ToList()` first.)

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test ... --filter "FullyQualifiedName~JoinedLiveBlockSplitsIntoStickyHeaderAndDescription" -v minimal`
Expected: FAIL — `LiveConversationHeader` doesn't exist, then no header emitted.

- [ ] **Step 3: Create the header item**

`LiveConversationHeader.cs` (mirror `ConversationHeader.cs` shape; keyed off `StartEntryLid` so it sorts at the block top):

```csharp
namespace ActualChat.UI.Blazor.App.Components;

public sealed class LiveConversationHeader : ChatMessage
{
    public LiveConversationHeader(Conversation conversation) : base(conversation.Id.StartEntryLid)
    {
        Conversation = conversation;
        ShouldSkipKey = true;
    }

    public override bool Equals(ChatMessage? other)
        => ReferenceEquals(this, other)
            || (other is LiveConversationHeader o
                && Conversation!.VersionEquals(o.Conversation)
                && Kind == o.Kind && Date == o.Date && Flags == o.Flags);

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Kind, Date, Flags);
}
```

- [ ] **Step 4: Emit the header before the card for the live block**

In `ChatUI.Tiles.cs`, `EmitConversationCard` (~802). Emit a `LiveConversationHeader` immediately before the `ConversationMessage` when the conversation is the live block (`conversation.Id == liveBlockId`). Confirm `liveBlockId` is in scope in `GetTile`; it is (used for the `c with { Id = renderBlockId }` substitution). Sketch:

```csharp
void EmitConversationCard(Conversation conversation, DateOnly date)
{
    if (conversation.Id == liveBlockId) {
        var header = new LiveConversationHeader(conversation) {
            Kind = ChatMessageKind.ConversationStart,
            Date = date,
            PreviousMessage = prevMessage,
        };
        if (prevMessage != null)
            prevMessage.NextMessage = header;
        messages.Add(header);
        prevMessage = header;
    }
    var message = new ConversationMessage(conversation) {
        // ... existing initializer unchanged ...
    };
    // ... existing wiring unchanged ...
}
```

(Read the current body of `EmitConversationCard` at `ChatUI.Tiles.cs:802` and insert the header emission at the top, keeping the existing `ConversationMessage` creation intact. `GroupExpandedConversations` already treats the live-block `ConversationMessage` as the block starter; the `LiveConversationHeader` has the same conversation id and lands inside the same block.)

- [ ] **Step 5: Render the header; make the card render only the description in the block**

`LiveConversationHeaderView.razor` — the sticky title band (title + live icon + chevron). Pull the title/fold/expand data via the same compute path the card uses. Minimal first cut (iterate visuals in Task 3):

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, LiveConversationHeaderState>

@{
    var s = State.Value;
    var title = s.Title.IsNullOrEmpty() ? s.ParticipantsText : s.Title;
}
<div class="live-conversation-header">
    <chat-activity-panel-icon-svg size="6" isActive="true" mode="audio"/>
    <span class="c-lc-name">@title</span>
    @if (s.HasFoldedEntries) {
        <HeaderButton Class="c-lc-expand" Click="@Toggle">
            <i class="@(s.IsExpanded ? "icon-collapse" : "icon-expand")"></i>
        </HeaderButton>
    }
</div>

@code {
    [Parameter, EditorRequired] public ChatContext ChatContext { get; set; } = null!;
    [Parameter, EditorRequired] public LiveConversationHeader Header { get; set; } = null!;
    // ComputeState mirrors the isLive/title/participants/hasFoldedEntries/isExpanded reads from
    // ConversationMessageView.ComputeState (LiveSessionUI + LiveBlockUI.GetBlockState +
    // ChatUI.ConversationExpansionOverrides). Define LiveConversationHeaderState as a record of
    // (string Title, string ParticipantsText, bool HasFoldedEntries, bool IsExpanded).
    private void Toggle() => Hub.ChatUI.ToggleExpandConversation(Header.Conversation!.Id);
}
```

In `ConversationMessageView.razor`, when the card is part of the live block (joined), drop the `.c-lc-title` block (now rendered by the header) and keep `.c-lc-summary-box` + `.c-lc-meta-row` + tail/join as the scrollable description. Guard with the existing `state.IsJoined` (the block/joined case) vs the unjoined standalone card, which keeps the full card unchanged. Concretely: wrap the current `.c-lc-title` emission (`ConversationMessageView.razor:31-41`) in `@if (!state.IsJoined) { ... }` so the joined block's title lives only in the sticky header, while the unjoined preview keeps it inline.

- [ ] **Step 6: Wrap the header as a sticky item + style it**

In `ChatView.razor`, the `ExpandedConversationMessage` loop wraps `ConversationHeader` children in `<div class="item expanded" data-skip="true">` (sticky). Extend that branch to also match `LiveConversationHeader`:

```razor
@if (childItem is ConversationHeader or LiveConversationHeader) {
    <div @key="@childItem.Key" class="item expanded" data-key="@childItem.Key" data-skip="true">
        @RenderChatMessage!((childItem, chatContext))
    </div>
}
```

Add the dispatch branch (near the `ConversationHeader` one):

```razor
else if (item is LiveConversationHeader liveConversationHeader)
{
    <LiveConversationHeaderView ChatContext="@chatContext" Header="@liveConversationHeader" />
}
```

`conversation.css` — give the band a solid background so the description occludes cleanly as it scrolls behind it (the `.item.expanded` wrapper already gets `sticky -top-px z-20` from `virtual-list.css:143`, and the author badges' `top-20/md:top-16` offset clears it):

```css
.virtual-list .c-virtual-container .live-conversation-header {
    @apply flex items-center gap-x-2;
    @apply px-4 py-2;
    @apply bg-01;              /* solid: occlude the summary scrolling underneath */
    @apply rounded-t-2xl;
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~LiveConversationDisplayTest" -v minimal`
Expected: PASS — all tests green (14), including both new tests. Investigate any `RenderKey` uniqueness failures (`ShouldNotDuplicateJoinedLiveCardAcrossTiles`) — the header and card share the conversation id but must have distinct `Key`s (header keyed off `StartEntryLid`, card off its own base). If keys collide, give `LiveConversationHeader` a distinct `Kind`/key suffix.

- [ ] **Step 8: Build + commit**

Run: `dotnet build ActualChat.CI.slnf` (0 errors) and `npm run build:Verify` (clean).

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeader.cs \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor \
        src/dotnet/UI.Blazor.App/Components/ChatView/ChatView.razor \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor \
        src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs \
        src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css \
        tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): split the joined live card into a sticky title band and scrollable description

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Browser verification + visual polish

The sticky/scroll/tint behavior is CSS and only truly verifiable in a browser. Drive two signed-in sessions, scroll an expanded live call, and iterate the CSS until it matches a regular expanded conversation with zero VirtualList violations. There is no unit test for this task — the deliverable is a verified visual + a `/virtual-list-debug` clean run.

**Files:**
- Modify (as needed from the browser pass): `conversation.css`, `live-conversation-footer.css`, `LiveConversationHeaderView.razor`, `chat-view.css` (only if the `top-20/top-16` offset needs a live-specific value).

- [ ] **Step 1: Confirm the server is up**

Run: `tail -5 tmp/watch-dotnet.log` and `curl -s -o /dev/null -w "%{http_code}" http://localhost:7080/` (expect `Now listening on:` / `200`). If Chrome debug isn't up, ask the user to run `ai chrome*2` (two sessions) — do not start it yourself.

- [ ] **Step 2: Load the driving skills**

Invoke `/debug-ui` and `/virtual-list-debug`. Sign in two users (`debugUI.signIn('+1 555 555 5550')` / `...5551`), open a shared chat, and enable the checker on both (`VirtualList.setDebugEnabled(true)`).

- [ ] **Step 3: Reach an expanded live call with enough entries to scroll**

Start recording on session A (fake mic), join on session B. Get a summary/title to land and enough author-alternating entries to scroll a full viewport. If the fake audio can't cross the 150-word first-summary gate (known limitation), ask the user to either point the fake mic at a longer speech file or temporarily lower `Summarization.MinLiveConversationWords/Entries` on their watch-owned server — do not change server config yourself.

- [ ] **Step 4: Verify the five visual checks**

Scroll the expanded live block and confirm, drain `debugUI.listVirtualListViolations(true)` after each:
1. Author badges pin below the title band and hand off between authors, exactly like a regular expanded conversation.
2. The title band stays pinned; the summary/description scrolls out of view behind it (solid background, no bleed-through).
3. The tint reads as an ongoing live block (not an ended box); the live footer sits at the bottom.
4. The summary appear/update height animation still runs cleanly on the (now scrollable) description.
5. `/virtual-list-debug` reports zero violations across the whole scroll on both sessions.

- [ ] **Step 5: Iterate CSS until all five pass, then commit**

Adjust the CSS files above as needed (background opacity, sticky offsets, footer band). Re-run `npm run build:Verify` after each change; the user's watch rebuilds the web bundle (watch for `tmp/watch-web.log`). When all five checks pass:

```bash
git add -A
git commit -m "style(live): polish sticky title band, tint, and footer against the browser

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Full verification sweep

- [ ] **Step 1: Build + suites**

```bash
dotnet build ActualChat.CI.slnf                    # 0 errors
npm run build:Verify                               # tsc + eslint + build clean
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~LiveConversationDisplayTest" -v minimal   # all green
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~LiveFoldMathTest" -v minimal                            # 5/5 (regression guard)
```

- [ ] **Step 2: Run the display suite 3× for flakiness** (the new tests join/scroll live state):

`for i in 1 2 3; do dotnet test ... --filter "FullyQualifiedName~LiveConversationDisplayTest" -v minimal --no-build; done` — expect stable all-green.

- [ ] **Step 3: Final whole-branch code review** (dispatch a `general-purpose` reviewer over the new commits) and address findings; then hand back to the user for the merge/PR decision (do not push).

## Risks & mitigations

- **`RenderKey` collisions** — the header, card, and footer all carry the live block's conversation id. They must produce distinct `Key`s or the VirtualList `OnlyHaveUniqueItems` invariant (`ShouldNotDuplicateJoinedLiveCardAcrossTiles`) breaks. Header keys off `StartEntryLid`, footer off `EndEntryLid`, card off its base; verify the three differ and adjust `Kind`/key if not.
- **`liveBlockId` scope in `EmitConversationCard`** — the plan assumes it's reachable in `GetTile`. Confirm at implementation; if not threaded in, pass it as a parameter to `EmitConversationCard`.
- **Unjoined preview regression** — the split must be gated to the joined/block case; the standalone unjoined card keeps its inline title. `RegularConversationsUnchanged`-style assertions and the existing tests guard this.
- **Frozen overlay block** — it renders under `liveBlockId` too, so it gets the header + footer as well; confirm `CloseKeepsRenderedItemsAndKey` / `ToggleAfterCloseCollapsesBlock` still pass (the footer/header must not change the frozen leaf lids or the `ConversationBlock:{V}` render key).
- **Sticky offset** — if the live title band is taller than a regular `ConversationHeader`, the author badges' `top-20/md:top-16` offset may need a live-specific value; adjust in Task 3 from the browser pass.
- **Tint without clip** — removing `overflow: hidden` means the tint must not rely on it for the rounded bottom; the footer's `rounded-b-2xl` + the group's `rounded-2xl` provide it. Verify no square-corner bleed in the browser.
