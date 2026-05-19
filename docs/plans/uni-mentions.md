# Universal Mentions — implementation record

**Status**: shipped on branch `feat/uni-mention` (5 commits, see `git log`).
All seven phases complete; build clean across `ActualChat.CI.slnf`;
`Chat.UnitTests` 327/327 pass; live-tested end-to-end via
`chrome-devtools` MCP against `server-loop`.

## Overview

A mention can now reference any of several entity kinds — users, chats,
places, emojis, GIFs — in addition to the legacy author kind. The wire
format stays `` @`<optional name>`<prefix>:<localId> ``; the prefix
selects a `MentionKind` from a central registry, the local id parses
into an `IMentionTarget`, and a typed `MentionMarkup` subclass carries
pre-resolved data so renderers can read it synchronously.

Out of scope (deferred):
- Message mentions (`ChatEntryId`).
- Channels — implicit via `ChatId` when a `ChatKind.Channel` ships.
- Cross-chat notifications for user mentions to non-members.

---

## 1. Mention syntax

### 1.1 Grammar (unchanged shape)

Two forms from `MarkupParser.cs`:

| Form | Pattern | Example |
|---|---|---|
| Unnamed | `@<id>` | `@a:chatid:1`, `@u:userId` |
| Named | `` @`<name>`<id> `` | `` @`Alice`a:chatid:1 `` |

`MentionMarkup.Format()` round-trips the same order — name first, then
id. Old stored markup renders verbatim.

### 1.2 `IdChar` widened

Before: `[A-Za-z0-9_\-:]`. Now: `[A-Za-z0-9_\-:.%~]` — added `.`, `%`,
`~` so URL-encoded local ids parse (RFC 3986 unreserved + `%` for
percent-escapes). No collisions with other markup tokens (`@`, `` ` ``,
`*`, whitespace).

### 1.3 Target set

| Prefix | Target | Suggested? | Render |
|---|---|---|---|
| `a` | `AuthorId` | no (legacy; existing markup keeps working) | `MentionView.razor` (existing author badge) |
| `u` | `UserId` | yes — contacts ∪ chat-author accounts | `UserMentionView.razor` (pill; strikethrough + tooltip for non-members) |
| `c` | `ChatId` | yes — caller's accessible chats | `ChatMentionView.razor` (`#<title>` pill, navigates to chat) |
| `p` | `PlaceId` | **no** (place picker chip dropped; mention by chat instead) | `PlaceMentionView.razor` (rendered when typed manually) |
| `e` | `EmojiRef` (URL-encoded glyph or slug) | yes — `Emojis.All` | `EmojiMentionView.razor` (inline glyph) |
| `g` | `GifRef` (URL-encoded picker id) | no (picker-only) | `GifMentionView.razor` (inline `<img>`/placeholder) |

---

## 2. Architecture as shipped

```
            ┌─ AuthorId   (prefix "a", legacy: parse + render only)
            ├─ UserId     (prefix "u")
IMentionTarget ─ ChatId    (prefix "c", covers group/place/thread chats)
            ├─ PlaceId    (prefix "p")
            ├─ EmojiRef   (prefix "e")
            └─ GifRef     (prefix "g")

MentionKind                 // sealed class with static instances + ByPrefix dict
  string Prefix             // "a" | "u" | "c" | "p" | "e" | "g"
  string Name
  TryParseTarget(s) -> IMentionTarget?

MentionId                   // not renamed (see deviation #1)
  string Value              // "<prefix>:<localId>"
  MentionKind Kind
  IMentionTarget Target
  PrincipalId? PrincipalId  // Target as PrincipalId — kept for back-compat

MentionMarkup (base, not sealed)
  MentionId Id
  string Name               // cached display name for first-paint
  static New(MentionId, string) -> MentionMarkup   // factory dispatching by Kind

AuthorMention : MentionMarkup { AuthorId AuthorId; Author? Author }
UserMention   : MentionMarkup { UserId UserId; Account? Account; bool IsChatMember }
ChatMention   : MentionMarkup { ChatId ChatId; Chat? Chat }
PlaceMention  : MentionMarkup { PlaceId PlaceId; Place? Place }
EmojiMention  : MentionMarkup { EmojiRef EmojiRef; string? Glyph; Picture? CustomPicture }
GifMention    : MentionMarkup { GifRef GifRef; Picture? Picture; int? Width; int? Height }

IChatMentionResolver
  ValueTask<MentionMarkup> Enrich(MentionMarkup, ct)  // single per-environment dispatcher
  ResolveAuthor / ResolveName (legacy shims)

IMentionResolver (non-generic)
  ValueTask<Markup> Apply(Markup, ct)   // markup-tree rewriter; renamed from MentionNamer
```

The seam is `MentionKind`. Adding a new kind = add a `MentionKind`
static instance + register the prefix + add a subclass + add a view.

---

## 3. Deviations from the original plan

1. **`MentionId` not renamed to `MentionRef`.** Wide mechanical rename
   was reverted because it churned 40+ files for no functional gain.
   The file's existing TODO marker remains; can be done later.
2. **No `MentionRenderInfo` envelope; no per-kind
   `IMentionRenderInfoProvider` registry.** Replaced by the typed
   `MentionMarkup` subclass + cached fields approach — each kind
   carries its own typed data shape, and a single
   `IChatMentionResolver.Enrich` dispatches per-kind in a `switch`.
   Cleaner for the renderer (synchronous reads), simpler to extend.
3. **No `MentionRefRegistry` as a separate type.** The `MentionKind`
   sealed class holds the registry as `ByPrefix` directly.
4. **Picker uses a single `MentionIndexUI` (Pattern A from the design
   discussion), not per-kind `IMentionSuggestionSource` plumbing.**
   The index builds the pool from contacts ∪ chat authors ∪ chats ∪
   emojis with user dedup by `UserId`. Splitting into per-kind sources
   stays an option for later.
5. **Place category chip dropped.** Places have a default chat; surface
   them as chats. So the chip bar is `All / U / C / E`.
6. **`EmojiNormalizer` keyed on `Emojis.BySymbol`, not a new
   `EmojiTable.cs`.** Only mentions whose decoded ref text matches the
   glyph (vanilla emojis) normalize to plain text; named slugs like
   `clown-yellow` stay as mentions so the editor treats them as atomic
   spans.
7. **`MentionNamer` renamed to `MentionResolver` (non-generic interface
   `IMentionResolver`).** Coexists with the existing typed
   `IMentionResolver<T>`. Both live in
   `Api/Chat/Markup/Visitors/MentionResolver.cs` and
   `Api/Chat/Markup/IMentionResolver.cs`.

---

## 4. File map

**New, in `src/dotnet/Api/`**:
- `Identifiers/MentionKind.cs`, `IMentionTarget.cs`, `EmojiRef.cs`, `GifRef.cs`.
- `Chat/Markup/AuthorMention.cs`, `UserMention.cs`, `ChatMention.cs`,
  `PlaceMention.cs`, `EmojiMention.cs`, `GifMention.cs`.
- `Chat/Markup/Visitors/EmojiNormalizer.cs`,
  `Chat/Markup/Visitors/MentionResolver.cs` (renamed from `MentionNamer.cs`).
- `Chat/MentionCandidate.cs`, `MentionCandidateKind.cs`, `MentionFilter.cs`.

**Modified, in `src/dotnet/Api/`**:
- `Identifiers/MentionId.cs` — `Target` + `Kind` (`MentionKind`); new
  factories `NewChat`/`NewPlace`/`NewEmoji`/`NewGif`.
- `Identifiers/UserId.cs`, `AuthorId.cs`, `ChatId.cs`, `PlaceId.cs` —
  implement `IMentionTarget`.
- `Chat/Markup/MarkupParser.cs` — widened `IdChar`; routes through
  `MentionMarkup.New`.
- `Chat/Markup/MentionMarkup.cs` — unsealed; `New(...)` factory.

**Frontend (UI.Blazor.App)**:
- New: `Services/MentionIndexUI.cs`,
  `Services/Internal/MentionIndexSearchProvider.cs`,
  `Components/MarkupParts/{User,Chat,Place,Emoji,Gif}MentionView.razor`.
- Modified: `Services/Internal/ChatMentionResolver.cs` (added `Enrich`),
  `Services/ChatMarkupHub.cs` (property rename + new search provider),
  `Components/MentionList/{MentionList.razor, MentionListManager.razor,
  mention-list.ts, mention-list.css}` (chips, multi-word, observer fix),
  `Components/MarkupEditor/markup-editor.ts` (space-no-dismiss),
  `Module/BlazorUIAppModule.cs` (DI + TypeMapper registrations).
- Deleted: `Services/MentionUI.cs`,
  `Services/Internal/ChatMentionSearchProvider.cs`.

**Backend (Chat.Service)**:
- `BackendChatMentionResolver.cs` — added `Enrich`.
- `BackendChatMarkupHub.cs` — property renames.

**Notification.Service**:
- `Notifications.cs:GetMentionedUserIds` — extended to include
  `UserMention` mentions resolved to chat members via
  `AuthorsBackend.GetByUserId`.

**Docs**:
- `docs/api-index.md`, `docs/api-index-full.md` updated.

**Tests**:
- `tests/Chat.UnitTests/MarkupParserTest.cs` — added 4 cases:
  `UniversalMentionKindsTest`, `UrlEncodedGifMentionTest`,
  `UnknownPrefixIsNotAMentionTest`,
  `EmojiNormalizerReplacesUrlEncodedGlyphWithGlyphTest`,
  `EmojiNormalizerLeavesCustomSlugAsMentionTest`,
  `EmojiNormalizerLeavesUnknownSlugsAsMentionsTest`.
- `tests/Chat.UnitTests/MentionFilterTest.cs` — 9 cases covering
  tokenization, multi-word prefix matching, kind ordering, member-first
  ranking, kind filter, coverage scoring, prefix-only behavior.

---

## 5. Phase notes (as shipped)

### Phase 1 — model & registry
Built `IMentionTarget`, `MentionKind` (sealed class with static
`Author`/`User`/`Chat`/`Place`/`Emoji`/`Gif` instances and `ByPrefix`),
`EmojiRef`, `GifRef`. Made `UserId`/`AuthorId`/`ChatId`/`PlaceId`
implement `IMentionTarget`. `MentionId.TryParse` dispatches via
`MentionKind.ByPrefix`. `IdChar` widened. `MentionId` kept its name;
`Target` (`IMentionTarget`) replaced `PrincipalId` as the canonical
typed accessor (`PrincipalId` kept as `Target as PrincipalId` for
back-compat).

### Phase 2 — typed subclasses + resolution
Introduced six `MentionMarkup` subclasses, each with kind-specific
cached fields. `MentionMarkup.New(MentionId, string)` dispatches by
kind. Parser produces subclasses. `IChatMentionResolver.Enrich(...)`
single dispatcher implemented in both `ChatMentionResolver` (frontend,
uses `IAccounts`/`IAuthors`/`IChats`/`IPlaces`) and
`BackendChatMentionResolver` (backend, uses the corresponding
`*Backend` services). Renamed `IMentionNamer`/`MentionNamer` →
`IMentionResolver`/`MentionResolver`; both interfaces (typed and
non-generic) coexist in `IMentionResolver.cs`. Hub property renamed:
`MentionNamer` → `MentionResolver` (the rewriter);
`MentionResolver` → `ChatMentionResolver` (the per-mention resolver).
`ApplyMentionNamer` extension → `ApplyMentionResolver`.

### Phase 3 — per-kind views
Five new view components registered in `BlazorUIAppModule`'s
`TypeMapper<IMarkupView>`. Existing `MentionView` stays as the fallback
for `AuthorMention` and unknown kinds. Views read cached fields
synchronously; fall back to `Markup.Name` then `NotAvailable`.

### Phase 4 — emoji normalization
`EmojiNormalizer : MarkupRewriter<Unit>` rewrites
`EmojiMention` → `PlainTextMarkup(glyph)` when the decoded ref text is
in `Emojis.BySymbol`. Wired into `ChatMarkupHubExt.PrepareForSave` so
messages persist with vanilla emojis baked in. Custom slugs
(`clown-yellow` etc.) and any unknown id keep the mention. Render-time
`Enrich` still populates `EmojiMention.Glyph` from `Emojis.ById` so
surviving mentions render correctly.

`EmojiRef.NewFromText(text)` URL-encodes via `WebUtility.UrlEncode`;
`EmojiRef.Text` decodes for lookup. Real emoji glyphs encode to `%XX`
only — no spaces to worry about; passing text with spaces through
`NewFromText` would break the parser (form-encoded `+`), flagged as a
known limitation.

### Phase 5 — kind-aware notifications
`UserMention.IsChatMember` (cached `bool`) populated by both enrichers:
frontend via `IAuthors.ListUserIds(session, chatId).Contains(userId)`,
backend via `AuthorsBackend.GetByUserId(...) is not null`.
`UserMentionView` applies `line-through` + tooltip "Not a member of
this chat" when `!IsChatMember`; click navigates to `Links.User(userId)`
for now (TODO marker for the richer "author info + invite" modal).
`Notifications.cs:GetMentionedUserIds` extended to include
`UserMention` targets whose user is a chat member; non-members not
notified. Chat/Place/Emoji/Gif: silent.

### Phase 6 — picker
`MentionIndexUI` (scoped fusion service) with one
`[ComputeMethod] GetPool(ChatId, ct)` that builds the candidate pool
from contacts (`Kind == User`) ∪ current chat's non-anonymous authors
∪ contacts (other kinds = chats) ∪ `Emojis.All`. User dedup by
`UserId`; primary name = peer-rename override else
`Account.Avatar.Name`; secondary = in-chat author name when it differs;
both indexed into `Words` for matching. Caller + guests excluded.

`MentionFilter` provides pure `Tokenize` / `MatchesAll` /
`CoverageScore` / `FilterAndRank`. Tokenization splits on whitespace +
ASCII punctuation; matching requires every query token to be a
case-insensitive prefix of some candidate word. Ranking:
Kind asc (User < Chat < Emoji) → `IsChatMember` desc → coverage desc →
PrimaryName asc.

`MentionIndexSearchProvider` adapts `MentionIndexUI` to
`ISearchProvider<MentionSearchResult>`; new overload accepts
`MentionKindFilter`. `MentionListManager` exposes
`MutableState<MentionKindFilter> KindFilter` and a `Find(...)` wrapper
that pipes the current filter through the index-backed provider.
`MentionList.razor` renders a sticky `All / U / C / E` chip bar above
the items; clicking a chip toggles the filter; the compute method
subscribes to `KindFilter.Use(...)` so chip changes re-run the search.

`markup-editor.ts`: `MentionListHandler.getMatchStart` no longer
terminates on space; only newline / `/` end the scan. So
`@John Bolton` keeps the picker open with the full filter.

`mention-list.ts` rewritten — old `MutationObserver` watched
`subtree: true, attributes: true` and stormed on every
`image-skeleton` class flip across 100 candidates. Replaced with a
Razor-driven `scrollSelectedIntoView()` call from `Selection`
setter / `MoveSelection` — fires once per actual selection change. No
observer needed.

### Phase 7 — docs
`docs/api-index.md` + `docs/api-index-full.md` refreshed with the new
types (subclasses, `MentionKind`, `IMentionTarget`, `EmojiRef`,
`GifRef`, `MentionCandidate`, `MentionFilter`, `EmojiNormalizer`,
`MentionResolver`, `MentionIndexUI`).

---

## 6. Verified end-to-end (live debug)

Tested via `chrome-devtools` MCP against the running `server-loop`:
1. `@` → picker shows 100 candidates with `All` chip active.
2. `@al y` → narrows to "Alex Y." and "Alex Yakunin" (multi-word prefix
   match works; picker survives the space).
3. `@smiling` → mixed list: 2 users first, then 5 emoji titles.
4. Click `E` chip → narrows to emojis only.
5. Click `U` chip → narrows to users only.
6. Select an emoji and post → chat shows the literal glyph (`😊`)
   — `EmojiNormalizer` ran at persist time.
7. Select a non-member user and post → chat shows the mention
   pill with `mention-markup-non-member` + `line-through` + tooltip
   "Not a member of this chat".
8. No `MutationObserver` storm in console from `_MentionList`.

---

## 7. Known follow-ups (deferred deliberately)

- **"Author info + invite" modal** for the non-member `UserMentionView`
  click target. Currently navigates to `/u/<id>`; a richer modal that
  conditionally shows an Invite button needs its own UI work.
- **`ChatEntryMessageView` "did mentioned members read my message"**
  indicator still extracts only `AuthorMention`s. Adding
  `UserMention`-of-member needs an async author lookup at a sync code
  site — separate refactor.
- **Standard-emoji slug names.** `Emojis.All` currently uses glyphs as
  ids for standard entries; only the URL-encoded glyph form makes
  standard emojis mentionable. Adding slug names (`smile`,
  `thumbs-up`, …) is a small but coordinated change touching reaction
  key storage. Filed as a separate task.
- **`EmojiRef.NewFromText`** uses form-encoding (`+` for spaces). Fine
  for glyphs; broken for arbitrary text with whitespace. Switch to
  `Uri.EscapeDataString` if a future caller needs space-tolerance.
- **`MentionId` → `MentionRef` rename.** Mechanical, ~40 files; the
  TODO in `MentionId.cs` flags it.
- **Per-kind `IMentionSuggestionSource`** plumbing. The current
  `MentionIndexUI.GetPool` is a single big switch; extracting per-kind
  sources is straightforward when extensibility is actually needed.
