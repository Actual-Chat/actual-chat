# Quote — reply to a message with only the selected text (#4274)

> Status: implemented on `feat/4274-quote-selected-text` (2026-09-11); unit,
> integration and e2e tests green.

## Goal

When some text inside a message is selected and the user opens that message's
context menu, offer a **Quote** item next to **Reply**. Quote works like Reply,
except the quote block above the new message shows only the selected fragment
instead of the whole replied message — the same thing Telegram does with a
partial quote.

## Current behavior

- **Reply** (`MessageMenuContent.razor`, `MessageHoverMenuContent.razor`) calls
  `ChatEditorUI.ShowRelatedEntry(RelatedEntryKind.Reply, entryId, focusOnEditor: true)`.
  The editor shows `RelatedChatEntryPanel` → `RelatedChatEntry`, which renders the
  replied entry's full text via `ToReadableText(MarkupConsumer.QuoteView)`.
- On send, `ChatMessageEditor` turns the `RelatedEntryRef` into
  `SendMessageRequest.ReplyMessage(chatId, entryId, text, uploads)`; the request is
  persisted (`SendMessageRequestEntry`), queued and posted as `Chats_UpsertEntry`
  with `RepliedEntryLid`. The server stores it in `DbChatEntry.RepliedChatEntryId`.
- A posted reply renders `ChatMessageQuote` above the message, again with the full
  text of the replied entry, loaded by `ChatEntryMessageView.GetRepliedEntry`.
- A message's context menu is `MessageMenu` (`ComputedMenuBase`). It is opened by
  `MenuHost` (`menu-host.ts`) from the `data-menu` attribute of the message root,
  of a nested link/code/image (which append extra arguments), and of the "more"
  button in the hover menu (`data-menu-trigger="Primary"`, which sits **outside**
  the message DOM). Before it shows a non-hover menu, `menu-host.ts` calls
  `unselect()` — on mobile this clears the browser selection, so the selection is
  only reliably available *before* `show()`.

So today nothing carries "which part of the replied message" anywhere: not the
editor state, not the send pipeline, not the entry.

## Design

### Data model — the quote is stored on the entry

The fragment must be visible to every reader, so it lives on the server:

- `ChatEntry.QuotedText` (`string?`, `[DataMember(Order = 18), Key(18)]`), placed
  under the existing `// Reply` group next to `RepliedEntryLid`. Sanitized like
  `Content`. Non-null only when `RepliedEntryLid` is set.
- `ChatEntryDiff.QuotedText` (`string?`) so `DiffEngine.DynamicPatch` in
  `ChatsBackend.ApplyDiff` carries it.
- `Chats_UpsertEntry.QuotedText` (`string?`, `Key(11)`). Honored on **create**
  only; on update it must equal the stored value or be null (same rule as
  "Replied entry Id cannot be changed").
- `DbChatEntry.QuotedText` → column `quoted_text` (`text`, nullable) + EF migration
  `Add_ChatEntry_QuotedText` in `Chat.Service.Migration`.
- `Constants.Chat.MaxQuotedTextLength = 1024`. The client truncates the selection
  to this length; the server rejects anything longer.

Old clients ignore the unknown MessagePack key and render the reply with the full
replied text — a graceful degradation, no version gate needed.

Alternatives rejected:

- **Client-only, prepend `> selected text` markup** (a `BlockQuoteMarkup`) to the
  message plus a plain reply. The message would then show both the full-text reply
  block and a blockquote, the quote would be editable/forgeable as ordinary body
  text, and it would not read as "reply with a fragment".
- **A `ChatEntryReply` sub-record** replacing `RepliedEntryLid`. Cleaner in theory,
  but it breaks every existing reader of `RepliedEntryLid` and the DB column for
  no functional gain. A sibling field mirrors how `Forwarded` fields sit beside
  the core ones.

### Capturing the selection — `MenuHost` remembers what was selected

The selection has to be read in JS at the moment the menu is triggered, before
`unselect()` (mobile) or the menu-item click (desktop) collapses it, and it must
be tied to the message it came from — the hover "more" button triggers the same
`MessageMenu` from outside the message subtree, and a right-click on a link inside
the selected text triggers a nested `data-menu` with extra arguments. Clamping to
the trigger element would therefore be wrong in both directions.

Mechanism, generic and living in `UI.Blazor`:

1. **`menu-host.ts`** — in `onClick`, before `this.show(menu)`, call
   `getSelectionOwner('menu')` (`dom-helpers.ts`): if the selection is
   non-collapsed, it finds the selection's *owner* — the nearest ancestor of the
   selected range carrying `data-menu` — clamps the range to it and returns the
   text. Pass `{ text, ownerMenuRef }` to Blazor as two
   extra `OnShowRequest` arguments (empty when there is no selection or no owner).
   A selection spanning two messages has the list as its common ancestor, which
   carries no `data-menu`, so it yields nothing — the natural "one message only"
   rule.
2. **`MenuHost.razor`** — `OnShowRequest(id, menuRef, isHoverMenu, selectedText,
   selectionOwnerMenuRef)` stores them on `MenuModel`, and the host exposes
   `MenuSelection? Selection` (`record MenuSelection(string Text, MenuRef OwnerMenuRef)`).
   Menus already get the host as `[CascadingParameter] MenuHost Host`, so no
   change to `IMenu`, `Menu<THub>`, `ComputedMenuBase` or the 16 menu components.
3. **`MessageMenu.razor`** — the selection belongs to this message when
   `Host.Selection?.OwnerMenuRef` is a `MessageMenu` ref whose first argument (the
   client entry id) equals `_clientEntryId`. That check covers all three trigger
   paths: message root (identical ref), nested link/code menu (owner is the
   message root because the selection's ancestor chain is resolved from the range,
   not from the click target), and hover "more" button (its `data-menu` string is
   built with the same arguments as the message root's). The matched text goes to
   `MessageModel.SelectedText` → `MessageMenuContent.SelectedText`.

Text normalization (JS): `range.toString()` of the clamped range, whitespace runs
collapsed to single spaces, trimmed, cut at `MaxQuotedTextLength`. Line breaks are
not preserved — the quote block is a single-line-clamped view anyway.

### Menu item

`MessageMenuContent.razor`, right above **Reply**:

```
canQuote = canReply && !SelectedText.IsNullOrEmpty()
<MenuEntry Icon="icon-quote" FocusOnEditor="true" Text="@L.MessageMenu_Quote" Click="@Quote"/>
```

`Quote()` → `ChatEditorUI.ShowQuote(ChatEntry.Id, SelectedText, focusOnEditor: true)`.
`icon-quote` is a new glyph: `src/nodejs/icons/quote.svg`, a rounded double
quotation mark drawn as a 2px stroke and outlined with `oslllo-svg-fixer`
(arcs written as cubic curves — the fixer emits an empty path for `A`
commands), then `npm run font`.

The hover menu keeps its single Reply button; the hover "more" button already
opens the full menu with Quote in it.

### Editor state and send pipeline

- `RelatedEntryRef` gains `[property: DataMember, Key(2)] string? QuotedText = null`.
  It is persisted per chat as a draft via `KvasExt.SetDraftRelatedEntryRef`;
  old stored values simply lack the key.
- `ChatEditorUI.ShowQuote(entryId, quotedText, focusOnEditor, updateUI)` builds a
  `Reply` ref with the quote. Reply and Quote share `Tune.ReplyMessage`.
- `RelatedChatEntryPanel.Model` and `RelatedChatEntry` get `QuotedText`; the
  `quote-text` div shows it instead of the entry's readable text when present.
- `SendMessageRequest.ReplyMessage(chatId, relatedMessageId, text, uploads, quotedText)`
  + `QuotedText` property. Both call sites in `ChatMessageEditor.razor` (regular
  send and the voice-mode send) pass `relatedChatEntry.QuotedText`.
- `SendMessageRequestEntry.QuotedText` (`Key(12)`), `PostMessageRequestInternal.QuotedText`,
  and the three copy points (`SendingMessages.StartStoredRequests.cs`,
  `SendingMessages.CreatePostMessageRequestInternal`, `SendingMessages.Queue.cs`
  building `Chats_UpsertEntry`).
- The optimistic sending entry (`ChatSendingMessagesAccessor`) doesn't set
  `RepliedEntryLid` today either, so it stays as is; the quote appears once the
  server confirms the entry, same as the reply block does now.

### Rendering a posted quote

- `ChatMessageQuote` gets `[Parameter] string? QuotedText`; when non-empty it
  renders that instead of `_text`. Author name, click-to-highlight and the
  removed/loading states are unchanged. `ChatEntryMessageView` passes
  `entry.QuotedText`.
- Removed replied entry: the quote block already shows "Message deleted"; keep
  that (the fragment is no longer verifiable).

### Server validation (`Chats.OnUpsertEntry`)

- `QuotedText` is trimmed; empty → null.
- Non-null `QuotedText` without `RepliedEntryLid` → `StandardError.Constraint`.
- Length > `MaxQuoteTextLength` → `RequireMaxLength`.
- On update: a non-null value that differs from the stored one →
  `StandardError.Constraint("Quoted text cannot be changed.")`; null leaves it.
- No substring check against the replied entry: the selection comes from the
  *rendered* text (mention names, link titles, collapsed whitespace), so it is
  not a substring of the raw `Content`. The author is quoting; they own the quote.

## Reuse

### Existing abstractions to reuse

| Need | Existing type / function |
|---|---|
| Find the `data-menu` owner of a DOM node | `getOrInheritData` (`src/nodejs/src/dom-helpers.ts`) |
| Menu argument transport | `MenuRef` (`UI.Blazor/Components/Menu/MenuRef.cs`) — `Parse`, `MenuType`, `Arguments` |
| Host ↔ menu channel | `MenuHost` cascading parameter already on `ComputedMenuBase` / `Menu<THub>` |
| Reply state | `RelatedEntryRef` / `EntryRef`, `ChatEditorUI.ShowRelatedEntry`, `KvasExt.Get/SetDraftRelatedEntryRef` |
| Send pipeline | `SendMessageRequest.ReplyMessage`, `SendMessageRequestEntry`, `SendingMessages.PostMessageRequestInternal`, `Chats_UpsertEntry` |
| Persistence | `ChatEntryDiff` + `DiffEngine.DynamicPatch` (no hand-written patching), `DbChatEntry.ToModel/UpdateFrom`, `ef-migrations.cmd Chat.Service add …` |
| Quote rendering | `ChatMessageQuote`, `RelatedChatEntry`, `LeftLine`, `AuthorName` |
| Sanitization / limits | `Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>`, `RequireMaxLength`, `Constants.Chat` |
| Sounds | `Tune.ReplyMessage` via `TuneUI` |
| Localization | `LocalizedStringsLocalizerExt`, `Strings.*.json`, `scripts/derive-bcms.cmd`, `scripts/derive-max.cmd` |

No existing helper clamps a DOM `Range` to an element or reports "which
`data-menu` owns the selection"; both are new (below).

### Reusability of new components

| New piece | Local vs shared | Recommendation |
|---|---|---|
| `getSelectionOwner(dataName): [owner, text]` — the selection's nearest `data-*` owner plus the selection clamped to it, whitespace-normalized | `menu-host.ts` vs `src/nodejs/src/dom-helpers.ts` | **Shared** (`dom-helpers.ts`, beside `getOrInheritData`). Any "act on selected text" feature — copy selection, share selection — needs the same clamp. |
| `MenuSelection` record + `MenuHost.Selection` | `UI.Blazor.App` vs `UI.Blazor/Components/Menu` | **Shared** (`UI.Blazor`), because `MenuHost` is generic and the owner-matching rule is expressed purely in `MenuRef` terms. |
| `ChatEntry.QuotedText`, `ChatEntryDiff.QuotedText` | — | `ActualChat.Api`, where the entry already lives. |
| `Constants.Chat.MaxQuoteTextLength` | — | `ActualChat.Api` `Constants.Chat`, beside `MaxEntryTextLength`. |
| `MessageMenu_Quote` string | — | `Localization` catalog, `MessageMenu_` group. |

## Steps

1. **Api / contracts** — `ChatEntry.QuotedText`, `ChatEntryDiff.QuotedText`,
   `ChatEntryDiff(ChatEntry)` copy, `Chats_UpsertEntry.QuotedText`,
   `Constants.Chat.MaxQuoteTextLength`.
2. **Server** — `DbChatEntry.QuotedText` (+ `ToModel`, `UpdateFrom`), migration,
   validation in `Chats.OnUpsertEntry`, pass-through into the create/update
   `ChatEntryDiff`.
3. **Menu host** — `getSelectionOwner` in `dom-helpers.ts`; capture in
   `menu-host.ts` `onClick`; `OnShowRequest` extra arguments and
   `MenuHost.Selection`.
4. **Message menu** — `MessageMenu.MessageModel.SelectedText` with the owner
   check; Quote item and `Quote()` in `MessageMenuContent`.
5. **Editor** — `RelatedEntryRef.QuotedText`, `ChatEditorUI.ShowQuote`, `RelatedChatEntryPanel` / `RelatedChatEntry` display, both
   `ChatMessageEditor` send sites.
6. **Send pipeline** — `SendMessageRequest`, `SendMessageRequestEntry`,
   `PostMessageRequestInternal`, the three copy points, `Chats_UpsertEntry`.
7. **Rendering** — `ChatMessageQuote.QuotedText`, `ChatEntryMessageView`.
8. **Localization** — `MessageMenu_Quote` = "Quote" in `Strings.en.json` (next to
   `MessageMenu_Reply`) and in the 18 other hand-written catalogs, typed member in
   `LocalizedStringsLocalizerExt`, then `scripts/derive-bcms.cmd` and
   `scripts/derive-max.cmd`.
9. **AOT** — no new components, so `BlazorUIAppAotSource.g.cs` needs no
   regeneration; `MenuSelection` is a plain record.
10. **Verify** — `npm run build:Verify` (or the `/server-loop` rebuild),
    `dotnet build ActualChat.CI.slnf`, tests below, then a manual pass with
    `/debug-ui`: select inside a message → right-click → Quote; select, hover →
    "more" → Quote; select across two messages → no Quote; mobile long-press
    selection → Quote; send, reload, confirm the fragment renders for a second
    account.

## Edge cases

- **Selection outside any message** (sidebar, header) → no owner → no Quote.
- **Selection inside the editor** (`contenteditable`) → owner is not a
  `MessageMenu` ref → no Quote.
- **Right-click on a link/code block inside the selection** → the nested
  `data-menu` opens with the link arguments *and* Quote, because ownership is
  resolved from the selected range.
- **Selection partly outside the message** (author name, timestamp, reply block)
  → clamped to the message root, so it may include the author name or the quoted
  parent's text. Acceptable for v1; if it bites, mark the markup container
  (`.chat-message-markup`) with a `data-menu-selection-scope` attribute and let
  the JS clamp to the innermost such element inside the owner.
- **System entries** — `canReply` is already false for them, so no Quote.
- **Streaming / sending entries** — `canReply` false; the sending-message menu
  (`SendingMessageMenuContent`) is untouched.
- **Edit of a quoted reply** — quote is preserved; the editor's Edit panel does
  not show it (it shows the message being edited, as today).
- **Draft restore** (`RestoreRelatedEntry`) — the persisted ref carries the quote,
  so reopening the chat restores "Quote: fragment" in the panel.
- **Quote length** — cut at 1024 chars client-side; the server enforces the same.
- **Thread start / forward from a quoted reply** — unchanged; they operate on the
  entry, and `Forwarded` entries do not carry the quote (forwarding copies
  `Content`, not the reply relationship).

## Tests

- `Chat.UnitTests/ChatCommandSerializationTest` — `Chats_UpsertEntry` round-trip
  with `QuotedText`; `ChatEntry` round-trip with `QuotedText`.
- `Chat.IntegrationTests/PostChatMessageTest` — reply with quote persists and
  reads back; quote without `RepliedEntryLid` is rejected; edit keeps the quote;
  edit with a different non-null quote is rejected; over-length quote is
  rejected.
- `Chat.UI.Blazor.UnitTests/AppLocalizationTest` — covers the new key
  automatically once every catalog has it.
- `tests/ts/e2e/quote-selected-text.test.ts` — select → context menu → Quote →
  send → quote block text, plus "no selection, no Quote"; run against
  `/server-loop` with `AC_E2E_SERVER=external`.

## Out of scope

- A dedicated Quote button in the hover menu.
- Preserving line breaks or markup (bold, links) inside the quoted fragment.
- Verifying the quote against the replied entry's content on the server.
- Highlighting the quoted fragment inside the parent message when the quote
  block is clicked (Telegram does this; needs the offset, which we don't store).
