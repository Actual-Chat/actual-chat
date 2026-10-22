# Triage card

The CI watchdog posts one per failure it triages, as a comment on the issue the failure
concerns. A person writing up a failure by hand can use the same shape. The patterns, the
actions and what a test fix may do are in
[`docs/testing/flake-patterns.md`](../../docs/testing/flake-patterns.md); this page is only the
card.

````markdown
<!-- watchdog-triage -->
**Triage** — `<FullyQualifiedTestName>` · [run <id>](<url>) · `<branch>` @ `<sha>`
Signature: `<test name> | <first line of the error, at most 80 chars>`

**What failed.** <the assertion or exception, and the line it came from>

**Evidence.** <the 3–8 log lines that matter, with timestamps>

**Mechanism (confidence: high | medium | low).** <how it fails, in terms of the code; the
pattern from flake-patterns.md, if one fits>
What would confirm it: <the one observation that would settle it, or "confirmed by the log above">

**Already fixed?** <"no: nothing after <sha> touches <files>" | the commit or PR that does>

**Proposed action:** fix-test | instrument | product-bug | wait | none — <one line why>

<details><summary>Draft change (untested)</summary>

```diff
<unified diff against origin/dev, files under tests/ only>
```
</details>
````

- `confidence: high` only when the log or the code settles the mechanism, not when it is
  merely plausible. At `low`, the action is `instrument` or `none`, never `fix-test`.
- One card per signature: a second failure with the same signature links the first card
  rather than repeating it.
- The draft is a proposal. Nothing runs it before a person picks it up.
- Once a card's outcome is known, a person reacts to the card: 👍 the proposed change (or an
  equivalent) fixed it, 😕 the mechanism was right but the fix was different, 👎 the cause was
  something else. No reaction means no outcome yet. The watchdog doesn't read them: they are
  collected later to measure how often the cards are right (#5546).
