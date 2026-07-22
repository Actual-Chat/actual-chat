# Live Block Unified Shell — Implementation Plan (Plan 1 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the live conversation block one shell across joined/unjoined/expanded and make its chrome correct (Join-to-record button in the sticky header, meta-row gated on a real summary, unjoined fixed-height faded preview, expanded = a plain conversation), leaving the fold *mechanism* unchanged.

**Architecture:** All changes are client-side rendering in `UI.Blazor.App`. The joined live block already renders through a sticky `LiveConversationHeaderView` + a `c-live-card` (`ConversationMessageView` live path) inside one VirtualList group. This plan (a) routes the **unjoined** live block through that same path so both share the shell, (b) fixes the card's chrome (meta-row, description, Join button, preview), and (c) composes join-with-recording. The fold boundary / swallow behaviour and the "Show more" pill are **Plan 2**.

**Tech Stack:** Blazor Server components (`.razor`), ActualLab.Fusion `ComputedStateComponent`, Tailwind-compiled CSS (`conversation.css`, `last-entries-preview.css`), xUnit integration tests (`Chat.UI.Blazor.IntegrationTests`, `ComputedTest.When` over `ChatUI.GetChatItems`).

## Global Constraints

- Read `docs/CODING_STYLE.md` before writing C#/TS/Razor. No `Async` suffix; no XML docs on members; comments only for non-obvious constraints; follow surrounding brace/format style.
- Build with `dotnet build ActualChat.CI.slnf`. TS/CSS-touching changes: `npm run build:Verify` (or the `/server-loop` rebuild if running — check `tmp/watch-dotnet.log`).
- A user-owned `dotnet watch` may own the server on port 7080 — do **not** `/server-start`/`/server-restart`.
- Branch `feat/live-block-unified-ux` (already created off `dev`, holds the spec commit). Do not push unless asked.
- Reuse before adding: activity-panel button classes for the Join button; existing `c-lc-meta-row`/`c-lc-authors`/`c-lc-started` markup; `LastEntriesPreview` for the preview; the `.group:has(…)` tint pattern. Spec: `docs/superpowers/specs/2026-07-22-live-conversation-block-unified-ux-design.md`.
- Real design tokens (light theme, captured from the running app): `--violet-60 #A533FF`, `--violet-60-10 #A635FF1A`, `--text-01/02 #1C1C1C`, `--text-03 #777777`, `--cr-item-badge-selected-text #582EFF`, unjoined tint `linear-gradient(90deg, rgba(255,202,255,.3) 2%, rgba(130,104,255,.3) 100%)`.

---

## File Structure

- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor` — the live `c-live-card`: description, meta-row, Join button, preview, expanded gating. **Primary edit surface.**
- `.../Conversation/LiveConversationHeaderView.razor` + `LiveConversationHeaderState.cs` — sticky header; gains the Join affordance for the unjoined block.
- `.../Conversation/ConversationLiveState.cs` — render-state record; may gain a `HasSummary` flag.
- `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` — routes the live conversation (joined **and** unjoined) through the sticky-header + card path (`GetTile` card emission ~L858-888, `hiddenLiveTailRange` ~L101-107, `GroupExpandedConversations` ~L965-1028).
- `src/dotnet/UI.Blazor.App/Components/ChatView/Items/LastEntriesPreview.razor` + `last-entries-preview.css` — fixed-height, bottom-anchored, top-faded preview.
- `.../Conversation/conversation.css` — shell tints, meta-row, Join button, description gating, header Join layout.
- Tests: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs` (extend).

Testing note: pure-CSS/layout changes (preview fade, tints, button look) are verified **manually** via chrome-devtools MCP against the running app (a live block is easy to spin up) — the existing test harness asserts the `ChatItems` model, not rendered pixels. Structural/logic changes (which message types are emitted, join action, state gating) get integration tests.

---

## Task 1: Join = join **and** record; label "Join"

The unjoined card's join action currently only starts listening. Spec §5: in the block it must start **recording** (active participation). Label stays "Join", no icon.

**Files:**
- Modify: `.../Conversation/ConversationMessageView.razor` (`OnJoin`, ~L249-252; the `c-join-row` button text ~L84)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `ChatAudioUI.SetRecordingChatId(ChatId? chatId, bool isPushToTalk = false)` (`ChatAudioUI.cs:163`) — starts recording in `chatId`, which makes the caller a recorder (so `AmIInLiveConversation` becomes true).
- Produces: `OnJoin()` now records; used by the header Join button in Task 5.

- [ ] **Step 1: Write the failing test** in `LiveConversationDisplayTest.cs` (pattern: `ClosedBlockRendersAsCompletedNotLive`). Arrange a live session the viewer has NOT joined, call the join, assert the viewer becomes a recorder.

```csharp
[Fact]
public async Task JoinFromBlockStartsRecording()
{
    // arrange - a live session this Bob has not joined
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "join-records-test");
    var peerId = AuthorId.New(chat.Id, 777_310);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    chatUI.SelectChatOnNavigation(chat.Id);

    // act - the block's Join action
    await chatAudioUI.SetRecordingChatId(chat.Id);

    // assert - Bob is now recording in this chat
    await ComputedTest.When(async ct => {
        var s = await chatAudioUI.GetState(chat.Id).ConfigureAwait(true);
        s.IsRecording.Should().BeTrue("Join from the live block starts recording");
    }, TimeSpan.FromSeconds(10));
}
```

- [ ] **Step 2: Run it and watch it pass or fail meaningfully.** This test pins the *behaviour we are wiring `OnJoin` to* (recording start), so it should already pass at the `ChatAudioUI` level — its role is a regression guard on the join-to-record contract. Run:

`dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~JoinFromBlockStartsRecording"`
Expected: PASS (confirms `SetRecordingChatId` ⇒ `IsRecording`). If it FAILS, the recording API differs — stop and reconcile before editing `OnJoin`.

- [ ] **Step 3: Change `OnJoin`** in `ConversationMessageView.razor` from listen to record:

```csharp
private async Task OnJoin() {
    var chatId = Message.Conversation!.Id.ChatId;
    await ChatAudioUI.SetRecordingChatId(chatId).ConfigureAwait(true);
}
```

- [ ] **Step 4: Confirm the button label is "Join"** (no icon). The current `c-join-row` reads "Tap to join" (`ConversationMessageView.razor:84`); change its text to `Join`. (Its relocation into the header and restyle happens in Task 5 — here only the label + action change.)

```razor
<button type="button" class="c-join-row" @onclick="@OnJoin">Join</button>
```

- [ ] **Step 5: Build + test.** `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -c Debug` (expect 0 errors), then re-run the Step 2 filter (expect PASS).

- [ ] **Step 6: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): block Join starts recording, label \"Join\""
```

---

## Task 2: Unjoined preview — 5 messages, fixed-height, bottom-anchored, faded

Spec §6. The preview is a self-contained region (not VirtualList swallowing): last ~5 messages, fixed height, newest pinned to the bottom, older lines fading out at the top so incoming messages never grow/jump the block.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/LastEntriesPreview.razor` (wrapper class)
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/last-entries-preview.css`
- Modify: `.../Conversation/ConversationMessageView.razor` — `GetThreadPreviewEntries` count (it currently takes 2)
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` — `GetThreadPreviewEntries` `.Take(2)` → `.Take(5)` (~L894-899)

**Interfaces:**
- Consumes: existing `LastEntriesPreview` component + `PreviewEntry` list from `GetTailPreview`.
- Produces: a `.last-entries-preview` region with fixed height + bottom anchor + top fade.

- [ ] **Step 1: Bump the preview entry count** to 5 in `ChatUI.Tiles.cs` `GetThreadPreviewEntries`:

```csharp
    public virtual async Task<IReadOnlyList<ChatEntry>> GetThreadPreviewEntries(
        ChatId chatId,
        CancellationToken cancellationToken = default)
        => await Chats.ReadReverse(Session, chatId, cancellationToken)
            .Where(x => !x.IsSystemEntry)
            .Take(5)
            .Reverse()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
```

- [ ] **Step 2: Make the preview fixed-height, bottom-anchored, faded** in `last-entries-preview.css`. Replace the `.last-entries-preview` rule so the region is a fixed-height flex column pinned to the bottom with a top opacity gradient (values validated in the visual companion):

```css
.last-entries-preview {
    position: relative;
    width: 100%;
    height: 8.25rem;              /* fixed: ~5 lines; incoming messages don't grow the block */
    overflow: hidden;
    display: flex;
    flex-direction: column;
    justify-content: flex-end;    /* newest line fully visible at the bottom */
    padding-bottom: 0.375rem;
}
.last-entries-preview::before {   /* older lines fade out under the card */
    content: "";
    position: absolute;
    inset: 0 0 auto 0;
    height: 3.75rem;
    z-index: 2;
    pointer-events: none;
    background: linear-gradient(to bottom, var(--background-01) 0%, transparent 100%);
}
```

Keep the existing per-entry `animation: lc-fade-in 200ms ease;` and `> *` rule.

- [ ] **Step 3: Verify manually** (pure CSS). With the running app: spin up a live conversation from a second identity, view it as a non-joined user, and confirm via chrome-devtools that `.last-entries-preview` has a fixed height, the newest line sits at the bottom, the top fades, and sending a new message does **not** change the block's height (no jump). Reference: this is the state A the companion mocked.

- [ ] **Step 4: `npm run build:Verify`** (CSS touched) — expect clean.

- [ ] **Step 5: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/last-entries-preview.css src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs
git commit -m "feat(live): fixed-height bottom-anchored faded unjoined preview (5 msgs)"
```

---

## Task 3: Expanded joined block = a plain conversation (no description, no header count)

Spec §8. When the block is expanded, the `c-live-card`'s description box must not render, and the sticky header must not show a count. (The sticky header today already has no count — this task guards it and drops the description on expand.) `state.IsExpanded` is already computed in `ConversationLiveState`.

**Files:**
- Modify: `.../Conversation/ConversationMessageView.razor` (the live-card description block ~L42-51)
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `ConversationLiveState.IsExpanded` (already present).
- Produces: no new interface.

- [ ] **Step 1: Write the failing test.** Extend `ClosedBlockRendersAsCompletedNotLive`-style setup, expand the block, and assert the rendered `ConversationLiveState` for the card has no description shown. Since the harness asserts `ChatItems`, assert at the model level that the block, when expanded, does not carry a description message and the header carries no count. Add to `LiveConversationDisplayTest.cs`:

```csharp
[Fact]
public async Task ExpandedLiveBlockHasNoDescription()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "expanded-no-desc-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_320);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
        Title = "Recap", Description = "a description", Summary = "s", EndEntryLid = v, MessageCount = 1,
    }, CancellationToken.None);
    for (var i = 0; i < 3; i++) await Tester.CreateTextEntry(chat.Id, $"m-{i}");

    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    await chatAudioUI.SetListeningState(chat.Id, true);
    chatUI.SelectChatOnNavigation(chat.Id);
    // ensure the block renders expanded
    var live2 = await Tester.Conversations.Get(Tester.Session, live.ToConversation().Id, CancellationToken.None);
    if (live2 is { IsExpandedByDefault: false })
        chatUI.ToggleExpandConversation(live.ToConversation().Id);

    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        var block = items.Items.OfType<ExpandedConversationMessage>().Single();
        // the card carries HasSplitHeader; when expanded it must not surface the description markup as a rendered row
        block.Items.OfType<ConversationMessage>().Single().HasSplitHeader.Should().BeTrue();
    }, TimeSpan.FromSeconds(15));
}
```

Note: description suppression is a Razor concern (the `c-live-card` markup), so the definitive check is manual (Step 3); this test guards the block still renders as one split-header block when expanded. If a cleaner model-level signal exists after implementation, tighten the assertion.

- [ ] **Step 2: Run it** — `dotnet test … --filter "FullyQualifiedName~ExpandedLiveBlockHasNoDescription"`. Expect PASS on the structural assertion (it does not yet test description suppression).

- [ ] **Step 3: Gate the description on collapsed.** In `ConversationMessageView.razor`, wrap the live-card description block so it renders only when the block is **not** expanded:

```razor
@* Description belongs to the collapsed recap only; expanded reads as a plain conversation. *@
@if (hasDescription && !state.IsExpanded) {
    <div class="c-lc-summary-box">
        <div class="c-lc-summary" @key="@m.Description.Markup.ToReadableText()">
            <CascadingValue Value="@_fakeEntry" IsFixed="true">
                <MarkupView Markup="@m.Description.Markup"/>
            </CascadingValue>
        </div>
    </div>
}
```

- [ ] **Step 4: Verify manually** the sticky header shows no count in either state and the description disappears when expanded (chrome-devtools against the running app). `LiveConversationHeaderView.razor` already renders only icon + title + expand — confirm no count leaks in.

- [ ] **Step 5: Build + test.** `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj -c Debug` (0 errors), re-run the Step 2 filter (PASS).

- [ ] **Step 6: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): expanded live block drops the description box"
```

---

## Task 4: Meta-row gated on a real summary (no "0 messages", no orphan heads)

Spec §3. Below the description, show the existing `c-lc-meta-row` (author heads + `c-lc-started` "Started at HH:MM · N messages") **only when a title/summary exists**. Pre-first-summary, render nothing there. This also removes the current title-less "0 messages" line and the duplicate roster.

**Files:**
- Modify: `.../Conversation/ConversationLiveState.cs` — add `bool HasSummary`
- Modify: `.../Conversation/ConversationMessageView.razor` — compute `HasSummary`; gate the meta-row
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `conversation.Title`, `conversation.MessageCount`.
- Produces: `ConversationLiveState.HasSummary` (`= title present OR MessageCount > 0`) used to gate the meta-row.

- [ ] **Step 1: Add `HasSummary` to `ConversationLiveState.cs`:**

```csharp
public sealed record ConversationLiveState(
    TranslatedConversation Conversation,
    bool IsLive,
    bool IsJoined,
    bool IsVoiceOnly,
    string ParticipantsText = "",
    bool HasFoldedEntries = false,
    bool IsExpanded = false,
    IReadOnlyList<PreviewEntry>? TailPreview = null,
    bool HasSummary = false);
```

- [ ] **Step 2: Populate it** in `ConversationMessageView.ComputeState` — a summary exists once there is a title (the first-summary gate always writes one):

```csharp
var hasSummary = !translated.Title.Text.IsNullOrEmpty();
return new ConversationLiveState(
    translated, isLive, isJoined, isVoiceOnly, participantsText, hasFoldedEntries, isExpanded, tailPreview, hasSummary);
```

- [ ] **Step 3: Gate the meta-row** in `ConversationMessageView.razor`. The live-card `c-lc-meta-row` (author heads + started/count) should render only when `state.HasSummary` and the block is not expanded. Wrap it:

```razor
@if (state.HasSummary && !state.IsExpanded) {
    <div class="c-lc-meta-row">
        <AuthorCircleGroup Class="c-lc-authors" AuthorIds="@conversation.AuthorIds"
            Size="@SquareSize.Size5" MaxCount="4" ShowRing="false"/>
        <div class="c-lc-meta">
            <div class="c-lc-started">Started at @startsAt.ToString("HH:mm") · @messageCount message@(messageCount == 1 ? "" : "s")</div>
        </div>
    </div>
}
```

(Reuse the existing meta-row markup already in the file; the only change is the surrounding `@if` and dropping the separate no-title icon/roster branch that produced "0 messages".)

- [ ] **Step 4: Write the failing test** — a live block with no summary must not surface the meta line. Add to `LiveConversationDisplayTest.cs`:

```csharp
[Fact]
public async Task PreSummaryBlockShowsNoMetaCount()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "pre-summary-meta-test");
    var author = await Tester.GetOwnAuthor(chat.Id).Require();
    var peerId = AuthorId.New(chat.Id, 777_330);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    await chatAudioUI.SetListeningState(chat.Id, true);
    chatUI.SelectChatOnNavigation(chat.Id);
    // no UpdateSummary => no title => HasSummary must be false on the rendered live conversation
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        var card = items.Items.SelectMany(i => i.GetLeafMessages())
            .OfType<ConversationMessage>().SingleOrDefault(c => c.Conversation!.Title.Text.IsNullOrEmpty());
        card.Should().NotBeNull("a pre-summary live block still emits its card");
        card!.Conversation!.MessageCount.Should().Be(0);   // the card carries 0; the view must NOT render "0 messages"
    }, TimeSpan.FromSeconds(10));
}
```

The "does not render 0 messages" outcome is a Razor gate (`HasSummary`), verified manually in Step 6; this test pins that a title-less card is what we gate on.

- [ ] **Step 5: Build + test.** `dotnet build … UI.Blazor.App.csproj` (0 errors); `dotnet test … --filter "FullyQualifiedName~PreSummaryBlockShowsNoMetaCount"` (PASS).

- [ ] **Step 6: Verify manually** — start a live conversation, and before any summary lands confirm the card shows no heads / no "0 messages"; after the first summary the meta row appears with the real count.

- [ ] **Step 7: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationLiveState.cs src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): gate meta-row on a real summary (no 0 messages)"
```

---

## Task 5: One shell — unjoined block renders through the sticky header + tinted card

Spec §1, §2, §5. Today the unjoined live block renders via the plain conversation-card path (no sticky header, `c-join-row` at the bottom). Route it through the same live-block path the joined block uses — a sticky `LiveConversationHeaderView` + the `c-live-card` — with the unjoined tint on the whole shell and the **Join** button in the header (activity-panel style). No expand affordance while unjoined.

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` — treat the live conversation as a split-header block whether or not joined (`GetTile` `EmitConversationCard` ~L858-888 `hasSplitHeader`; `GroupExpandedConversations` ~L965-1028 so the unjoined block is one group with a header + footer; `hiddenLiveTailRange` stays hiding the un-summarized tail so the card shows its preview).
- Modify: `.../Conversation/LiveConversationHeaderView.razor` + `LiveConversationHeaderState.cs` — render a **Join** button (instead of the expand chevron) when the viewer is not joined.
- Modify: `.../Conversation/ConversationMessageView.razor` — remove the now-redundant bottom `c-join-row` (join lives in the header).
- Modify: `.../Conversation/conversation.css` — apply the unjoined tint to the unified shell wrapper; style the header Join button; drop the old `c-join-row` block if unused.
- Test: `tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs`

**Interfaces:**
- Consumes: `OnJoin()` (Task 1); `ConversationLiveState.IsJoined`; the `.group:has(> .item > .conversation-message.live:not(.joined))` selector for the unjoined tint.
- Produces: an unjoined live block that emits a `LiveConversationHeader` + one `ExpandedConversationMessage`, same as joined.

- [ ] **Step 1: Write the failing test** — an unjoined live block must now emit a `LiveConversationHeader` (today it does not). Add to `LiveConversationDisplayTest.cs`:

```csharp
[Fact]
public async Task UnjoinedLiveBlockUsesStickyHeader()
{
    await Tester.SignInAsUniqueBob();
    var (chat, _) = await Tester.CreateAndGetChat(false, "unjoined-shell-test");
    var peerId = AuthorId.New(chat.Id, 777_340);
    var peer2Id = AuthorId.New(chat.Id, 777_341);
    var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
    await liveBackend.OnStreamRegistered(chat.Id, peerId, null, true, CancellationToken.None);
    await liveBackend.OnStreamRegistered(chat.Id, peer2Id, null, true, CancellationToken.None); // 2+ => SessionStartedAt latches
    var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
    var v = live!.EffectiveVisibleStartLid;
    await liveBackend.UpdateSummary(chat.Id, new LiveSessionSummary {
        Title = "Recap", Description = "d", Summary = "s", EndEntryLid = v, MessageCount = 1,
    }, CancellationToken.None);

    var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
    chatUI.SelectChatOnNavigation(chat.Id);   // Bob is NOT recording/listening => not joined
    var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
    var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);

    await ComputedTest.When(async ct => {
        var items = await chatUI.GetChatItems(chat.Id, query, 0, ct);
        items.Items.SelectMany(i => i.GetLeafMessages())
            .OfType<LiveConversationHeader>().Should().ContainSingle(
                "the unjoined live block shares the sticky-header shell");
    }, TimeSpan.FromSeconds(15));
}
```

- [ ] **Step 2: Run it** — expect FAIL (no `LiveConversationHeader` for the unjoined block today):
`dotnet test … --filter "FullyQualifiedName~UnjoinedLiveBlockUsesStickyHeader"`.

- [ ] **Step 3: Route the unjoined live conversation through the live-block path** in `ChatUI.Tiles.cs`. The live block id must be the live conversation's id whether or not joined, so the split header + grouping apply. Change the `else` branch that sets `liveBlockId` (currently `liveBlockId = joinedLiveConversation?.Id;`, ~L97) to use the live conversation's id when there is a live session:

```csharp
        else {
            // The live block uses the same shell joined or not; only the tint + header affordance differ.
            liveBlockId = liveConversation?.Id;
            liveBlockFoldRange = liveFoldRange;
        }
```

Then in `EmitConversationCard`, `hasSplitHeader` already keys on `conversation.Id == liveBlockId && materializedBlockId == null`, so the unjoined block now gets the sticky header. Verify `GroupExpandedConversations`'s `startsBlock`/`belongs` (which key on `liveBlockId`) now wrap the unjoined block into one `ExpandedConversationMessage` with the header + footer — the `liveBlockRange` open-ended `[V, ∞)` grouping already covers "no BlockEndLid" (still-live) blocks. Keep `hiddenLiveTailRange` hiding the un-summarized tail for the not-joined viewer (unchanged, ~L101-107) so the card renders its `TailPreview` rather than the live tail.

Run the Step 1 test — expect PASS. If grouping mis-nests (duplicate `@key` or the card outside a block), adjust `GroupExpandedConversations` so a `ConversationMessage` whose `Conversation.Id == liveBlockId` starts a block even when not in `expandedConversations` (it already does via `conversation.Id == liveBlockId`).

- [ ] **Step 4: Add the Join affordance to the sticky header.** In `LiveConversationHeaderState.cs` add `bool IsJoined`; in `LiveConversationHeaderView.ComputeState` set it from `LiveSessionUI.AmIInLiveConversation`; in the markup, show a **Join** button when not joined instead of the expand chevron:

```razor
<div class="@cls">
    <chat-activity-panel-icon-svg size="6" isActive="true" mode="audio"/>
    <span class="c-lc-name">@title</span>
    @if (!s.IsJoined) {
        <button type="button" class="c-lc-join btn btn-sm btn-transparent" @onclick="@Join">Join</button>
    }
    else if (s.HasFoldedEntries) {
        <HeaderButton Class="c-lc-expand" Click="@Toggle">
            <i class="@(s.IsExpanded ? "icon-collapse" : "icon-expand")"></i>
        </HeaderButton>
    }
</div>
```

Add the `Join` handler (mirrors Task 1):

```csharp
private async Task Join()
    => await Hub.ChatAudioUI.SetRecordingChatId(Header.Conversation!.Id.ChatId).ConfigureAwait(true);
```

- [ ] **Step 5: Remove the redundant bottom join row** from `ConversationMessageView.razor` (the `@if (!state.IsJoined) { <button class="c-join-row" …>Join</button> }` block) — join now lives in the header.

- [ ] **Step 6: CSS — unjoined tint on the unified shell + header Join button** in `conversation.css`. Extend the existing joined `.group:has(…)` tint rule (captured at `conversation.css`) with an unjoined variant that paints the whole shell with the unjoined gradient, and give the header its rounded top for the unjoined case too:

```css
.virtual-list .c-virtual-container .group:has(> .item > .conversation-message.live:not(.joined)),
.virtual-list .c-virtual-container .group:has(> .item.expanded > .live-conversation-header):has(.conversation-message.live:not(.joined)) {
    isolation: isolate;
    border-radius: 1rem;
    background: linear-gradient(90deg, rgba(255, 202, 255, 0.3) 2%, rgba(130, 104, 255, 0.3) 100%);
}
.live-conversation-header .c-lc-join {
    flex: 0 0 auto;
    height: 2.5rem;
    padding: 0 1rem;
    border-radius: 0.5rem;
    color: var(--violet-60);
    background-color: var(--violet-60-10);
}
```

(If reusing the app's `btn btn-sm btn-transparent` gives the exact activity-panel look with no extra CSS, prefer that and drop the `.c-lc-join` background/height overrides — verify against the panel's real button captured earlier: violet-60 text, 8px radius, 40px, `rgba(255,255,255,.1)`-style transparent bg.)

- [ ] **Step 7: Build + verify.** `dotnet build … UI.Blazor.App.csproj` (0 errors); `npm run build:Verify` (CSS). Re-run all touched tests:
`dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"` (all PASS).

- [ ] **Step 8: Verify manually** (chrome-devtools) — as a non-joined viewer the live block now shows the sticky header + unjoined tint + a **Join** button in the header + the fixed-height faded preview; tapping Join starts recording and the block flips to the joined tint. Watch the viewport across the join transition; use `/virtual-list-debug` if anything jumps.

- [ ] **Step 9: Commit.**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderView.razor \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/LiveConversationHeaderState.cs \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor \
  src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/conversation.css \
  tests/Chat.UI.Blazor.IntegrationTests/LiveConversationDisplayTest.cs
git commit -m "feat(live): unify joined/unjoined shell — sticky header + header Join + unjoined tint"
```

---

## Verification (whole plan)

1. `dotnet build ActualChat.CI.slnf` — 0 errors.
2. `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~LiveConversationDisplayTest"` — all PASS (existing + the four new tests).
3. `npm run build:Verify` — clean.
4. Manual two-identity pass against the running app: unjoined shell (tint + header Join + faded fixed preview, no jump on new messages); Join → records + flips to joined tint; pre-summary shows no "0 messages"/heads; after summary the meta-row appears; expand → plain conversation (no description, no header count); collapse returns to the card. Use `/virtual-list-debug` at each transition.

## Self-review notes (spec coverage)

- §1 unified shell + unjoined tint → Task 5. §2 sticky header (icon+title+chevron/Join) → Task 5 (header already minimal). §3 meta-row gated → Task 4. §5 Join = record, no icon → Task 1 + Task 5 (placement). §6 fixed-height faded preview → Task 2. §8 expanded = no description/count → Task 3.
- **Deferred to Plan 2 (governor rewrite):** §4 swallow-above-viewport and §7 the "Show N of M" straddling pill. These require reworking `LiveFoldMath.Advance` / `LiveBlockUI.ProcessChat` (boundary tracks the viewport-top lid instead of summary-driven `PendingFold`s + `FoldLag`) and a reader-controlled "revealed count", with `/virtual-list-debug` verification — a different risk class, kept out of this plan so the shell can ship independently.

---

## Plan 2 (to be written next): Collapsed swallow + Show-more

Outline for the follow-up plan, once Plan 1 lands:

1. **Swallow-above-viewport (§4).** Rework `LiveBlockUI.ProcessChat` (`LiveBlockUI.cs:266-283`) + `LiveFoldMath.Advance` so the fold boundary = the viewport-top lid (`minVisibleLid`), advanced monotonically, instead of ripening summary-driven `PendingFold`s behind `FoldLag`. Preserve the no-jump invariant (folding above-viewport rows, growing the card). Unit-test the new `LiveFoldMath` rule (pattern: `LiveFoldMathTest.cs`); integration-test the fold range in `ChatUI.Tiles`; verify with `/virtual-list-debug`.
2. **Revealed-count reveal (§7).** Add a per-chat "revealed" offset on `LiveBlockUI` that retreats the boundary by N (≈ one viewport of messages); resolve the exact batch measure and the reveal lifecycle (persist until expand/re-collapse) — the two open questions the spec flags.
3. **Show-more pill UI.** A rounded pill straddling the collapsed card's bottom edge, label "▲ Show N earlier messages of M" (N = batch, M = swallowed remaining from the true swallowed count), reusing the `.show-more-btn` colour + the Call/Map switch pill visual; wired to the reveal action.
