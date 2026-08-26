---
title: Localization size budgets
description: Declare how much room a string has, measure every translation against it, and shorten the ones that do not fit.
---

# Localization size budgets

Today a translation is correct if it means the right thing. Nothing checks
whether it *fits*. The Max-locale audit found twelve root causes of layout
breakage, and the fixes were all defensive — make the box cope with any length.
That is right as a floor, but it is the wrong place to solve the whole problem:
a phrase that needs two lines in a one-line box is usually just a phrase that
could have been shorter.

**The principle.** Where two phrasings are equally accurate and the box is
width-constrained, use the shorter one — and record why, so the next translator
does not helpfully lengthen it back.

::: warning Equally accurate, then shorter
Russian `Установить приложение Voxt` → `Скачать приложение Voxt` saves real
width, but *install* → *download* is a small meaning shift. In an app-store
context they are near-synonymous and the trade is fine. The rule is **equally
accurate and shorter**, never merely shorter — otherwise this degrades into
approximation.
:::

[[toc]]

## Why a budget, not a rule of thumb

"Keep it short" is unactionable: short compared to what? The useful question is
always *how much room does this string actually have*, and that differs per key:

| Element | What actually constrains it |
|---|---|
| Multi-line (banner body, modal text, empty-state sentence) | Nothing hard. It wraps. Only the **size relative to English** matters — 4x means a wall of text where English had a line. |
| Single-line (tab label, button, badge, settings row) | A real px budget: the **narrowest** box this string is ever rendered in. |
| A row shared with a sibling | A budget on the **pair**, not on either alone — a label with a button beside it fits only if `label + button` fits. |

That last case is why a per-key constant is not enough, and why the budget
has to be expressible as a small formula.

## The three pieces

### 1. Size data, emitted by the existing measurer

`scripts/l10n/derive-max.py` already measures every value in every catalog with
real glyph advances from the checked-in `TT-Commons-Pro-Regular.ttf`, with
Unicode-aware estimates for characters outside it (wide CJK glyphs, zero-width
combining marks) and with placeholders and markup stripped. It is the engine
this needs; nothing new has to be written to measure.

Its `width()` returns **raw font design units**. Dividing by `unitsPerEm` gives
**em**, which is what the budget should be denominated in — em is independent
of the element's font size, so one constant stays correct whether the string
lands in `text-caption-1` or `text-headline-1`.

Emit `Strings.sizes.json` beside the catalogs:

```jsonc
{
  "ChatList_TabAll": { "en": 1.62, "pl": 6.44, "ja": 2.10, "…": 0 },
  "Common_Skip":     { "en": 2.05, "de": 6.71, "…": 0 }
}
```

### 2. Budgets, declared next to the English source

The person who knows the layout is the person editing the component, so the
constraint belongs in `Strings.en.json` as a `//` comment — the file already
uses this convention for translator guidance, and comments survive derivation
into `cnr`/`hr`/`sr` and into generated `max`.

```jsonc
// fits: this <= 5.4em            -- the tab strip is width-constrained
"ChatList_TabAll": "All",

// fits: this + Banner_AddMembers <= 19.6em
"Banner_OnlyPersonInChat": "You're the only one in this chat",

// fits: this <= 2.5x en          -- multi-line, so only the ratio matters
"Bubble_ManageAccountTitle": "Your account and settings",
```

Three forms, which cover every case the audit found:

| Form | Meaning |
|---|---|
| `this <= <n>em` | single-line box of a known width |
| `this + Other_Key <= <n>em` | the pair shares a row |
| `this <= <n>x en` | multi-line; bound the ratio, not the absolute |

### 3. A checker

`scripts/l10n/check-sizes.py` reads the constraints and the size data and
reports every language that busts a budget. Cheap to run, deterministic, no
browser. Once it is trusted it becomes an `AppLocalizationTest` case, so a
translation that does not fit fails the build the way an untranslated key
already does.

## Where the numbers come from

A budget is the **narrowest** box the string is rendered in, measured once in
the browser:

```js
const el = document.querySelector('.tab-panel-tabs .btn-group');
el.clientWidth / parseFloat(getComputedStyle(el).fontSize);   // -> em budget
```

The Max audit already produced these for the constrained surfaces — the banner
row's 314px usable width, the chat-list strip's 316px, the right-panel strip's
320px, the settings tab rail, the bubble footer. Those become the first
constants.

## Doing it

1. Emit `Strings.sizes.json` from `derive-max.py`. Smallest step, immediately
   useful on its own — it answers "which translations are the widest" without
   any constraint syntax at all.
2. Write budgets for the keys the audit already implicated: `ChatList_Tab*`,
   `RightPanel_Tab*`, `Common_Skip`/`Common_Next`, `Banner_AddMembers` +
   `Banner_OnlyPersonInChat`, `Settings_DeveloperTools`, `Chat_PublicChat`,
   `Call_JoinMuted`, the transcription option formats.
3. Add the checker; run it; see what falls out.
4. Shorten the violations, each with a `//` note recording the constraint.
5. Re-run `derive-max` — **Max narrows as a result**, because Max is by
   definition the widest of the shipped translations. Shortening the worst
   offender directly relieves every layout downstream.
6. Promote the checker into `AppLocalizationTest`.

## Known limits

- **A key can render in more than one place.** The budget is the narrowest, so
  some keys will be constrained by a surface nobody thinks of. Being wrong here
  is visible and cheap to correct; being silent is not.
- **Budgets drift when layouts change.** The comment lives next to the string,
  not next to the CSS that justifies it. Mitigation: the checker fails loudly,
  and the constraint says which element it came from.
- **Font fallback is approximated** for scripts outside TT Commons Pro. Already
  true of Max generation, and the estimates are Unicode-aware, but a CJK budget
  is less exact than a Latin one.
- **This does not replace the layout fixes.** A box still has to survive a
  string that busts its budget — budgets reduce how often that happens, they do
  not guarantee it never does.

## Related

- [Localization (i18n)](../i18n.md) — the catalog rules and the Max pseudo-locale.
- [Max-locale layout findings](./max-locale-findings.md) — the audit whose
  measurements seed the first budgets.
