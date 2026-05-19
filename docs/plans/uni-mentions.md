# Implementation Plan: Universal Mentions

## Overview

Today a mention can only refer to an `AuthorId` (legacy) or a `UserId`. We're generalizing the mention machinery so a mention can reference **any "mentionable" entity** — users, chats, places, future channels, emojis, and GIFs — through a single uniform model: a `MentionRef` whose value is a registry-dispatched prefixed string.

Out of scope:
- Message mentions (`ChatEntryId`) — dropped from v1.
- Channels — handled implicitly when chats grow a `ChatKind.Channel`.

---

## 1. Mention syntax

### 1.1 Existing grammar (unchanged in shape)

`MarkupParser.cs:142-155` defines two forms:

| Form | Pattern | Example |
|---|---|---|
| Unnamed | `@<id>` | `@a:chatid:1`, `@u:userId` |
| Named | `` @`<name>`<id> `` | `` @`Alice`a:chatid:1 `` |

`Format()` (`MentionMarkup.cs`) round-trips the same order: name first, then id. We **keep** this order. Old stored markup continues to render verbatim.

### 1.2 Widening `IdChar`

Current `IdChar` (`MarkupParser.cs:45-46`): `[A-Za-z0-9_\-:]`. We add **`.`**, **`%`**, **`~`** to support URL-encoded local ids (RFC 3986 unreserved + `%` for percent-escapes). New class: `[A-Za-z0-9_\-:.%~]`.

Safe because none of those chars collide with mention/style tokens (`@`, `` ` ``, `*`, whitespace).

### 1.3 New mention-target set

| Prefix | Target | Suggester | Render |
|---|---|---|---|
| `a` | `AuthorId` | no (legacy) | author badge (existing) |
| `u` | `UserId` | yes — chat members + caller's contacts where `Kind == User && State != Blocked` | user badge |
| `c` | `ChatId` | yes — chats the caller can read | chat pill, click → open chat |
| `p` | `PlaceId` | yes — places the caller is a member of | place pill, click → open place |
| `e` | `EmojiRef` | maybe (defer decision) | inline glyph |
| `g` | `GifRef` | no (inserted by GIF picker only) | inline GIF media |

---

## 2. Architecture

```
            ┌─ AuthorId   (prefix "a", legacy: parse + render only, never suggested)
            ├─ UserId     (prefix "u")
IMentionTarget ─ ChatId    (prefix "c", covers group/place/channel chats)
            ├─ PlaceId    (prefix "p")
            ├─ EmojiRef   (prefix "e")
            └─ GifRef     (prefix "g")

MentionRef
  string Value         // full "<prefix>:<localId>"
  string Prefix        // e.g. "u"
  string LocalId       // the part after the colon
  IMentionTarget Target // typed, resolved via MentionRefRegistry

MentionMarkup
  MentionRef Id
  string Name   // cached display name for first-paint

Per kind (DI-registered):
  IMentionSuggestionSource   // produces picker candidates
  IMentionRenderInfoProvider // resolves MentionRef → {name, avatar, click, hasRead}
```

The registry is the seam. Adding "channels" later = a new `IMentionTarget` + a registered source + a registered view. No core code touched.

---

## 3. Reuse section

**Existing abstractions to reuse**:
- `MentionMarkup` — shape unchanged (`Id` + `Name`).
- `MarkupParser` mention grammar (`MarkupParser.cs:142-155`) — dispatches through the new registry instead of hardcoded `a:`/`u:`.
- `MarkupHtmlFormatterBase.VisitMention` — already prefix-agnostic; no change.
- `MentionExtractor` — already walks any `MentionMarkup`; no change.
- `MarkupRewriter` / visitor infrastructure — used by the emoji persist-time normalizer.
- `IAccounts.Get(Session, UserId, ct)`, `IAuthors.Get`, `IChats.Get`, `IPlaces.Get`, `IContacts.ListIds/Get` — resolution sources.
- `AuthorBadgeTemplate`, `AuthorCircle`, `image-skeleton`, `Avatar`, existing CSS `mention-markup*` classes.
- `SearchPhrase`/`SearchMatch` ranking used in `MentionUI.Find`.
- `StringIdentifier` base type for the new `EmojiRef` and `GifRef`.

**New components & placement**:
- `MentionKind`, `IMentionTarget`, `MentionRef` (renamed from `MentionId`), `MentionRefRegistry`, `EmojiRef`, `GifRef` → `src/dotnet/Api/Identifiers/`. No server/UI deps; correctly shared the first time.
- `IMentionSuggestionSource` (interface) → `src/dotnet/Api/Chat/Markup/`. Implementations live next to their domain service.
- `IMentionRenderInfoProvider` + `MentionRenderInfo` → `src/dotnet/UI.Blazor.App/Services/Mentions/`. UI-bound (Session, navigation).
- Per-kind Blazor views (`UserMentionView`, `ChatMentionView`, `PlaceMentionView`, `EmojiMentionView`, `GifMentionView`) → `src/dotnet/UI.Blazor.App/Components/MarkupParts/Mentions/`.

---

## 4. Phasing

Each phase ships green before the next starts. Single commit per phase.

### Phase 1 — Model & registry (additive)

1. `MentionKind` — sealed-class-with-known-instances keyed by prefix string. Not an `enum` because we want third-party extension. Each instance has `Prefix`, `DisplayName`, and a `TryParse` delegate.
2. `IMentionTarget` — marker interface.
3. `MentionRefRegistry` — static registration; `Register(MentionKind kind, Func<string, IMentionTarget?> tryParse)`. Initialized in a single `ModuleInit`-style static ctor invoked from `Api`'s module.
4. Make `UserId`, `AuthorId`, `ChatId`, `PlaceId` implement `IMentionTarget` and self-register.
5. Add new identifiers:
   - `EmojiRef` — `StringIdentifier` wrapping the emoji slug (e.g. `smile`, `:custom-emoji-id`).
   - `GifRef` — `StringIdentifier` wrapping a URL-encoded picker id.
6. Rename `MentionId` → `MentionRef` (the file's existing TODO at `MentionId.cs:12`). Wide but mechanical. Add `MentionRef.Prefix`, `MentionRef.LocalId`, `MentionRef.Target`. Keep all factory methods (`NewAuthor`, `NewUser`) and add `NewChat`, `NewPlace`, `NewEmoji`, `NewGif`.
7. `MentionRef.TryParse` dispatches via the registry. Existing `a:`/`u:` tests still pass.
8. Widen `IdChar` in `MarkupParser.cs:45-46` to `[A-Za-z0-9_\-:.%~]`.
9. **Tests** in `tests/Chat.UnitTests`:
   - Parse round-trip for each kind.
   - Invalid prefix → parse failure.
   - URL-encoded GIF id round-trip.
   - Named-mention round-trip per kind.
   - Existing `MentionTest`/`NamedMentionTest` continue passing.

**No UI/render changes in this phase.** Existing renderer keeps rendering only Author/User mentions.

### Phase 2 — Resolution layer

1. Define `MentionRenderInfo`:
   ```csharp
   public sealed record MentionRenderInfo(
       string DisplayName,
       string? AvatarUrl,
       string? Tooltip,
       MentionClickAction? OnClick,
       bool? HasRead);
   ```
2. `IMentionRenderInfoProvider`:
   ```csharp
   public interface IMentionRenderInfoProvider
   {
       MentionKind Kind { get; }
       ValueTask<MentionRenderInfo?> Resolve(
           MentionRef mention, ChatEntry entry, CancellationToken ct);
   }
   ```
3. Per-kind providers in `UI.Blazor.App/Services/Mentions/`:
   - `AuthorMentionRenderInfoProvider` — wraps today's `ChatMentionResolver` author path.
   - `UserMentionRenderInfoProvider` — uses `IAccounts.Get(session, userId, ct)`.
   - `ChatMentionRenderInfoProvider` — `IChats.Get`.
   - `PlaceMentionRenderInfoProvider` — `IPlaces.Get`.
   - `EmojiMentionRenderInfoProvider` — looks up the unicode emoji table for known slugs; returns `DisplayName = "<glyph>"` for known, `null` (fallback to `Markup.Name`) for unknown custom ones.
   - `GifMentionRenderInfoProvider` — resolves the picker id to media metadata.
4. `MentionRenderInfoProviderResolver` — DI-keyed dispatcher (`IReadOnlyDictionary<MentionKind, IMentionRenderInfoProvider>`).
5. `ChatMentionResolver` becomes a thin shim over the dispatcher; preserve `IMentionResolver<Author>` for legacy callers.

### Phase 3 — Rendering (per-kind views)

1. `MentionView.razor` switches on `Markup.Id.Target` and delegates to a per-kind sub-component. Each sub-component:
   - Uses `Markup.Name` for first paint (no flicker).
   - Asynchronously resolves the live `MentionRenderInfo` via `MentionRenderInfoProviderResolver` and updates.
   - Falls back to "(n/a)" pill if unresolvable / inaccessible.
2. Author view = today's `AuthorBadgeTemplate` behavior (preserved verbatim).
3. `EmojiMentionView` — renders the resolved glyph inline; no pill chrome.
4. `GifMentionView` — renders `<img>` or `<video>` inline.
5. HTML formatter unchanged (already prefix-agnostic).

### Phase 4 — Emoji normalization

1. Add a unicode-emoji table (slug → glyph) — pick an existing curated list (e.g., `emoji-data` minimal subset). Lives in `src/dotnet/Api/Chat/Markup/EmojiTable.cs`.
2. `EmojiNormalizer` — a `MarkupRewriter` that replaces `MentionMarkup(EmojiRef)` with `PlainTextMarkup(glyph)` **only when the slug is in the unicode table**. Custom (non-unicode) emoji refs are left as mentions.
3. Wire `EmojiNormalizer` into the persist-time pipeline (likely `Chats.Service`'s upsert path). **Render-time substitution remains** — `EmojiMentionRenderInfoProvider` still resolves unicode slugs to glyphs, so a mention that survived to render also renders correctly.

### Phase 5 — Notification dispatch

1. Today's mention-driven notification path is hardcoded to authors. Make it kind-aware:
   - `UserId` mention → notify that user (if member of the chat).
   - `AuthorId` mention → existing behavior.
   - `ChatId`/`PlaceId`/`EmojiRef`/`GifRef` → decorative; no notification.
2. `Mention` persistence record (`Api/Chat/Mention.cs`) unchanged — already stores any `MentionId` string.

### Phase 6 — Suggestion UI

1. `IMentionSuggestionSource`:
   ```csharp
   public interface IMentionSuggestionSource
   {
       MentionKind Kind { get; }
       Task<IReadOnlyList<MentionCandidate>> Find(
           ChatId chatId, SearchPhrase phrase, int limit, CancellationToken ct);
   }
   ```
2. Sources for v1:
   - **UserMentionSuggestionSource**: chat authors with disclosed accounts ∪ caller's `IContacts` peer contacts where `Kind == User && State != Blocked`. Anonymous chat authors excluded. Caller self excluded.
   - **ChatMentionSuggestionSource**: chats the caller has read access to.
   - **PlaceMentionSuggestionSource**: places the caller is a member of.
3. `MentionUI.Find` aggregates from registered sources, ranks by combined `SearchMatch.Rank`, returns `MentionSearchResult[]` with `MentionRef` (any prefix).
4. `MentionList.razor` renders per-kind row template.
5. Optional prefix-as-filter UX: `@c…` narrows to chats, `@u…` to users, `@p…` to places.
6. **Author suggestion source removed** — per direction, author mentions are legacy-render-only.

### Phase 7 — Cleanup & docs

- Update `docs/api-index.md` entries for the renamed `MentionId → MentionRef` and the new types.
- Delete dead code (old `MentionUI` author-only path).

---

## 5. Test coverage targets

- **Phase 1**: parser/format round-trip for every kind incl. URL-encoded GIF id; named & unnamed forms.
- **Phase 2**: per-kind resolver returns expected `MentionRenderInfo` (unit tests with mocked `IAccounts`/`IChats`/`IPlaces`).
- **Phase 3**: snapshot/visual sanity per kind (manual via `/qa` skill once UI lands).
- **Phase 4**: emoji normalizer test — unicode slug substituted, custom slug preserved.
- **Phase 6**: `ChatMentionSearchTest` extended — user mentions returned, blocked contacts excluded, chat & place candidates surface, anonymous authors filtered.

---

## 6. Non-goals & deferred

- Message (`ChatEntryId`) mentions — out.
- Cross-chat notifications for user mentions to non-members — separate, larger work.
- Migrating existing `a:` author mentions to `u:` user mentions in stored markup — none.
- Globally searching all users (suggester stays scoped to contacts + chat).
- Channels — implicit via `ChatId` when channel chat-kind ships.
