# Hashtags (#4121)

## TLDR

Add first-class `#hashtag` support in three phases:

1. **Markup + click-to-search (MVP, client + Api only).** A new
   `HashtagMarkup : TextMarkup` parsed by `MarkupParser`, rendered by a new
   `HashtagMarkupView` as a clickable link. Clicking it opens the left-panel
   search, puts `#hashtag` into the search field, sets the type filter to
   Messages, and runs the existing search. No DB, no new backend. Search
   quality is "whatever OpenSearch does with the word today" (the `#` is
   stripped by the analyzer, so it finds the word `hashtag` anywhere).
2. **Exact hashtag search.** A `HashtagExtractor` visitor (mirror of
   `MentionExtractor`) feeds an exact-match hashtag index, and a single-
   `#hashtag` search criteria queries it instead of the full-text match.
   **Where that index lives depends on timing**: OpenSearch is slated for
   wholesale retirement in favor of the Postgres-based `Search.Service`
   ([mlsearch-postgres-fts.md](./mlsearch-postgres-fts.md), decisions agreed
   Jul 2026). Preferred: fold hashtag token preservation into the new
   service's application-level analyzer (trivial there); only if exact
   hashtag search is needed *before* that lands, add a small throwaway
   `Hashtags` keyword field to the OpenSearch `IndexedEntry`.
3. **(Deferred, optional) Hashtag registry in DB.** A `Hashtags` table
   mirroring `Mentions` (`DbMention` / `MentionsBackend` pattern), enabling
   `#`-autocomplete in the editor, per-chat hashtag lists, and usage counts.
   Not needed for phases 1–2; decide after the MVP ships.

Phase 1 is fully shippable on its own and delivers everything the issue asks
for; phase 2 makes the search *correct*; phase 3 is a product decision.

## Current state (verified against source)

- **Parsing.** `MarkupParser` (`src/dotnet/Api/Chat/Markup/MarkupParser.cs`)
  is a Pidgin combinator grammar. `#` is currently *not* a special char:
  `NotSpecialOrWhitespaceChar` consumes it, so `#tag` is plain text today.
  `#` only matters at line start as a header token (`HeaderLevel`), and a
  header requires whitespace after the `#`s — so `#tag` (no space) never
  collides with `# Title`.
- **Markup model.** `Markup` (`Markup.cs`) is a MessagePack union with 18
  registered subtypes; `TextMarkup` subtypes are dispatched in the generic
  visitors' `VisitText` switch (`Visitors/Generic/MarkupVisitor.cs:21`,
  plus `MarkupVisitorWithState` ×2 and `AsyncMarkupVisitor`). An unknown
  subtype falls into `VisitUnknown`, which throws — so every visitor base
  needs a `VisitHashtag` case.
- **Rendering.** `MarkupView` resolves a Blazor component per markup type via
  `TypeMapper<IMarkupView>` registered in
  `BlazorUIAppModule.cs:100` — adding a view is one `.Add<,>()` line plus a
  `.razor` file in `Components/MarkupParts/`.
- **Mentions precedent for extraction + storage.** `MentionsBackend`
  (`Chat.Service/MentionsBackend.cs`) listens to `ChatEntryChangedEvent`,
  re-parses the entry, extracts ids with `MentionExtractor`
  (a `MarkupVisitorWithState<HashSet<MentionRef>>`), and diffs them into the
  `Mentions` table (`Db/DbMention.cs`). This is the exact template for a
  phase-3 hashtag registry.
- **Search.** Left-panel search field (`LeftSearchPanel/LeftChatSearchInput.razor`)
  writes into `SearchUI.Text`; `SearchUI.StateSync` debounces, queries
  `ISearch.FindEntries` → `MLSearch.Service/SearchBackend.FindEntriesInOpenSearch`,
  which runs a `MatchBoolPrefix` on `IndexedEntry.Content` (a standard-analyzed
  `text` field, mapping in `OpenSearchConfigurator.ConfigureEntryIndex`).
  The standard analyzer drops `#`, so searching `#promo` today ≈ searching
  `promo`. ACL filtering is done by restricting to the user's chat ids
  (`ListChatIds`), and that part is reusable as-is for hashtag search.
- **Editor.** The message editor round-trips markup through
  `MarkupEditorHtmlConverter` (a `MarkupHtmlFormatterBase`). Hashtags need no
  editor-side special casing in phase 1: they can render as plain editable
  text in the editor and re-parse on send/save.

## Design decisions

### Markup: `HashtagMarkup : TextMarkup`

- `Text` holds the **full token including `#`** (e.g. `#promo`), so
  `Format()` is inherited behavior returning `Text` and copy/edit round-trips
  are trivial. A computed `Tag` property returns `Text[1..]`.
- New `TextMarkupKind.Hashtag` enum member; `Kind` override returns it.
- `[Union(19, typeof(HashtagMarkup))]` on `Markup`; `[DataContract,
  MessagePackObject]` like the siblings. Run `App.AotHelper -g` after adding
  the type (AOT serializer registry).
- Not a `MentionMarkup`: mentions carry a resolvable `MentionRef` identity and
  a resolver pipeline; a hashtag is just text with behavior. `TextMarkup` fit
  keeps every text-ish code path (trimming, normalizing) sane.

### Token grammar

`#` followed by a tag body, recognized only at word start (which the grammar
gives us for free — mid-word `#`, as in `C#5` or `item#2`, stays inside the
plain-text run because `#` remains a non-special char for `NotSpecialOrWhitespaceChar`):

- Body: first char is a Unicode letter or `_`, then letters, digits, `_`, `-`.
- All-digit tokens (`#4121`) are **not** hashtags — they're issue/number
  references people paste all the time.
- Max length 64 chars; longer → plain text.
- `# Title` stays a header (hashtag parser fails on the space, header wins at
  block level). `#tag` at line start becomes a hashtag — behavior change from
  "plain text", which is the point.
- Inside code blocks / preformatted spans nothing changes (those parsers
  consume first).
- Matching is case-preserving for display; comparison/search lowercases
  (invariant) — same choice every major chat product makes.

Implementation: a `Hashtag` parser added to the `nonStylizedMarkup` chain in
`InternalParsers.Build` (before `NonWhitespaceText`), analogous to `Mention`.
The token is atomic, so the incomplete-markup grammar variants need nothing
extra.

### Click behavior

New `HashtagMarkupView.razor` renders `<a class="hashtag-markup">#tag</a>`.
On click:

1. `PanelsUI.Left.SetIsVisible(true)` — brings the left panel up on mobile;
   no-op on desktop.
2. `SearchUI.PlaceId.Value` ← current place (same rule as
   `LeftChatSearchInput.OnClick`), `SearchUI.ShowRecent(true)` to open the
   search panel.
3. `SearchUI.Text.Value = markup.Text` — the existing
   `SearchUI.StateSync.SyncSearch` debouncer picks it up and runs the search;
   `IsSearchModeOn` flips automatically once results land.
4. `SearchUI.SetTypeFilter(SearchTypeFilter.Messages)` — hashtags live in
   messages; the badge lets the user widen it back.

One wrinkle: `LeftChatSearchInput` reads `SearchUI.Text.Value` (deliberately
not `.Use()`, see the `AY: why?` comment at `LeftChatSearchInput.razor:99`),
so an externally-set text may not appear in the input box if the panel is
already open. Fix by publishing a UI event (mirror of the existing
`SearchClearedEvent` → e.g. `SearchTextSetEvent`) that the input handles by
setting its `TextInput` content. The whole click handler belongs in `SearchUI`
(e.g. `SearchUI.SearchFor(string text)`) rather than in the view, so other
callers (deep links, place search overlay) can reuse it.

### Storage: is a DB table warranted?

Investigated the `Mentions` precedent. Verdict: **not for search** — search
already flows through OpenSearch with ACL handling, and duplicating that in
Postgres means building a parallel result pipeline for `FoundChatEntry`.
A keyword field on the existing entry index (phase 2) is strictly less code
and lands in the existing results UI untouched.

A DB registry (phase 3) becomes worth it only for features OpenSearch serves
poorly: `#`-autocomplete while typing, per-chat "browse by hashtag", counts /
trending. The `MentionsBackend` + `DbMention` pattern transfers 1:1
(`DbHashtag { ChatId, EntryLid, Tag }`, same event handler, same diffing).
Defer until those features are actually wanted.

## Reuse

**Existing abstractions to reuse:**

- `MarkupParser` grammar + `SafeTryOneOf` combinator infra — extend, not fork.
- `TextMarkup` / `TextMarkupKind` — base for the new markup.
- Generic visitor bases (`MarkupVisitor`, `MarkupVisitorWithState` ×2,
  `AsyncMarkupVisitor`, `MarkupRewriter`, `AsyncMarkupRewriter`) — add the
  `VisitHashtag` hook once in the bases; rewriters get identity defaults like
  `VisitUrl` already has.
- `MentionExtractor` — template for `HashtagExtractor` (phase 2/3).
- `TypeMapper<IMarkupView>` + `MarkupViewBase<T>` — view registration/dispatch.
- `SearchUI` (`Text`, `PlaceId`, `ShowRecent`, `SetTypeFilter`) +
  `UIEventHub` events (`SearchClearedEvent` pattern) — click-to-search.
- `PanelsUI.Left.SetIsVisible` — mobile panel handling.
- `SearchBackend.FindEntriesInOpenSearch` incl. `ListChatIds` ACL scoping and
  `OpenSearchConfigurator.ConfigureEntryIndex` — phase 2 lands inside them.
- `MentionsBackend` / `DbMention` — phase 3 template, if it happens.

No existing abstraction covers "hashtag" itself — verified by repo-wide
search (`rg -i hashtag`): nothing outside icon fonts.

**Reusability of new components:**

- `HashtagMarkup`, parser rule, `TextMarkupKind.Hashtag`, `HashtagExtractor`
  → `src/dotnet/Api/Chat/Markup/` next to their siblings. That project *is*
  the shared home for markup; nothing to promote.
- `HashtagMarkupView` → `UI.Blazor.App/Components/MarkupParts/` with the
  other markup views (they are only used via `TypeMapper`, already shared
  across all app targets).
- `SearchUI.SearchFor(text)` — placed on `SearchUI` (shared UI service)
  precisely so it's reusable beyond the hashtag click.
- No TypeScript components are needed (rendering and click handling are
  Blazor; the TS editor treats hashtags as plain text).

## Phase 1 — markup + click-to-search

**Status: implemented** (Aug 2026, `feat/4121-support-hashtags`). Notable
deltas from the design below: `TextMarkupKind.Hashtag` was appended *after*
`Unknown` to keep existing enum wire values stable; `TextInput` gained a
`SetText` method (backed by the previously unused JS `set`) instead of a
custom sync path; a hex color like `#f3f4f6` is an accepted hashtag
false positive (pinned in `SpecialTest_CssRuleCase` — round-trip text stays
exact); and tags must be whitespace-separated — `#a#b` is plain text, not
two tags (a trailing `Lookahead(Not('#'))` guard backtracks the whole run).

1. `Api/Chat/Markup/HashtagMarkup.cs` — new type as designed above;
   `TextMarkupKind.Hashtag`; `Union(19)` on `Markup`.
2. `MarkupParser` — `Hashtag` parser in the `nonStylizedMarkup` chain.
3. Visitor bases — `HashtagMarkup` case in the `VisitText` switches; abstract
   `VisitHashtag` in visitor bases, identity default in the rewriter bases.
   Concrete visitors (all 1-liners):
   - `MarkupFormatter` — append `Text` (both variants).
   - `MarkupHtmlFormatterBase` — emit `<span class="hashtag">`-wrapped text;
     `MarkupEditorHtmlConverter` inherits it and needs nothing extra (plain
     editable text in the editor is the phase-1 behavior).
   - `MentionExtractor`, `LinkExtractor` — no-op.
   - `MarkupTrimmer` — atomic like `VisitUrl` (don't split the tag).
   - `MarkupValidator` — predicate hook.
   - `MarkupNormalizer` — must *not* merge a hashtag into adjacent plain text
     (different `Kind` should already prevent it; add a test).
4. `App.AotHelper -g` regeneration.
5. `HashtagMarkupView.razor` + CSS (style ~like `mention-markup`) +
   `TypeMapper` registration in `BlazorUIAppModule`.
6. `SearchUI.SearchFor(string text)` + `SearchTextSetEvent` + handler in
   `LeftChatSearchInput`; wire the view's click to it.
7. Tests:
   - `MarkupParserTest`: `#tag`, `#tag-two_3`, mid-word `c#5`/`item#2` (not a
     tag), `#4121` (not a tag), `# Title` (header), `#tag` at line start,
     `**#tag**`, `` `#tag` `` (preformatted, not a tag), 64+ chars, `#тег`
     (Unicode), `#a#b`.
   - `MarkupFormatterTest` / `MarkupNormalizerTest` round-trips.
   - Serialization round-trip test alongside the existing markup
     serialization tests in `tests/Chat.UnitTests`.

## Phase 2 — exact search

The search engine is in flux: OpenSearch/MLSearch will be **retired
wholesale** and replaced by a Postgres FTS `Search.Service` with
application-level text analysis ([mlsearch-postgres-fts.md](./mlsearch-postgres-fts.md)).
That plan already indexes **raw tokens** alongside stems — exact hashtag
search there is just (a) making the tokenizer keep `#tag` as one raw token
(the standard behavior would split it) and (b) routing a single-hashtag
criteria to a raw-token exact match. Both are small, and belong *in that
plan* — when phase 2 is approved, add a "hashtag tokens" note to the
Search.Service analyzer spec rather than building anything hashtag-specific.

Common piece, buildable now:

1. `Api/Chat/Markup/Visitors/HashtagExtractor.cs` — mirror of
   `MentionExtractor`, returns lowercased tags. Used by whichever indexing
   pipeline is current, and by phase 3 if it happens.

Only if exact search is wanted **before** `Search.Service` lands (throwaway,
~3 files, drop at MLSearch retirement):

2. `IndexedEntry.Hashtags : string[]` + keyword mapping in
   `OpenSearchConfigurator.ConfigureEntryIndex`; populate in the
   `ToIndexedEntry` mapping used by `EntryIndexingFlow`.
3. `SearchBackend.FindEntriesInOpenSearch` — when `query.Criteria` is exactly
   one hashtag token, replace the `MatchBoolPrefix` on `Content` with a
   `Term` on `Hashtags` (keep the chat-id parent filter). Highlighting: keep
   highlighting `Content` with the raw tag text.
4. Mapping migration: adding a field to an existing index is a compatible
   put-mapping change; existing documents simply lack the field until the
   entry is re-indexed. Check how `OpenSearchConfigurator.EnsureIndex` treats
   an existing index (update vs create-only) — if create-only, bump the entry
   index version / trigger the existing reindex flow.
5. Integration test: index entries with `#promo` and with plain `promo`,
   assert `#promo` finds only the former and `promo` finds both.

## Phase 3 (deferred) — DB registry + autocomplete

Only if `#`-autocomplete / per-chat hashtag browsing / trending is wanted:
`DbHashtag` + `HashtagsBackend` on the `MentionsBackend` template, editor
autocomplete on `#` reusing the `MentionListManager` + `MentionList` machinery
with a hashtag candidate source. Not designed further here on purpose.

## Open questions

- Click scoping: current plan searches with the ambient place scope +
  Messages filter. Alternative: scope to the *current chat*
  (`SearchLocationFilter.Chat`). Recommendation: ambient scope — matches what
  the search field itself would do, and the filter badges make narrowing one
  tap away.
- Should `#tag` in a *voice transcript* (`PlayableTextMarkup`) be linkified
  too? Phase 1 doesn't touch transcript rendering; transcripts rarely contain
  literal `#`. Skip until asked.
