Prod incident found 2026-09-02: `MarkupParser` (Pidgin grammar, `src/dotnet/Api/Chat/Markup/MarkupParser.cs`)
backtracked exponentially on unbalanced style tokens because `CreateStylized` recurses through
`Rec(() => TextBlock)` with every alternative wrapped in `Try`. `*` x 36 took 7.5 s, x4 per 4 extra chars.
v2.15 had the same shape, so it was latent, not a regression of the 2.16 parser perf work.

Trigger: a user in group chat `VCmaZX6h1z` (author `VCmaZX6h1z:1`) pasted an e-mail with a 106-asterisk
divider on 2026-08-26 ~04:xx UTC and kept re-sending it. Each `ChatsBackend_ChangeEntry` ran
`PrepareTextEntryForSave -> ChatMarkupHubExt.PrepareForSave -> parse` and never returned, pinning a thread
at 100% until the liveness probe restarted the container (staircase CPU chart). Nothing was persisted.

Fix (2026-09-02, branch `fix/markup-parser-backtracking`, PR to dev): `MemoParser` + `ParseMemo` memoize
text blocks per position (linear, same output); `StyleTokenRunText` treats 4+ `*`/`|` as text;
`ParseBudget` (thread-static step counter spent in `SafeOneOfParser`) throws past
`2000 + 200 * length` steps and `ParseRaw` falls back to plain text. A depth cap was tried first and
rejected: it changed results for shallow inputs because backtracking explores deeper than the real nesting.
Also fixed on the way: `StringExt.ParseLines` dropped every line but the last (ExplicitCapture + unnamed group).

**Why:** explains the Aug 26+ CPU plateaus/restarts, and why a depth cap is the wrong fix for this grammar.
**How to apply:** for any new parser hang, check the calibration guard test
`RegularMessagesShouldStayFarBelowTheStepBudget` and the bounded-time theory in `MarkupParserTest`.
See [[prod-live-diagnostics]].
