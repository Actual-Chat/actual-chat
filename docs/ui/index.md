---
title: UI
description: Blazor/TypeScript UI documentation — component conventions, the virtual list, safe areas, and splash screens.
---

# UI

Everything that is strictly about the client UI: how components are written, and
the three UI surfaces complex enough to need a document of their own.

## Conventions

- [Component guidelines](./components.md) — file structure for `.razor` / `.css` /
  `.ts` triples, CSS naming and the `@apply` rules, `ComputedStateComponent`
  patterns, JS interop, animation performance.

## Components

- [The virtual list](./virtual-list.md) — the complete specification of
  `InfiniteList` (the chat transcript) and `FiniteList` (the sidebar): vocabulary,
  invariants, the state machine, the overscroll model, and the browser/device
  quirks that shaped them. Pairs with the `/virtual-list-debug` skill, which is
  how to *measure* what this document specifies.

## Layout and startup

- [Safe areas](./safe-areas.md) — `viewport-fit=cover`, the four
  `--safe-area-*` variables, which layers may extend under the insets, and how to
  visualize them.
- [Splash screens](./splash-screen.md) — the native splash and the web splash,
  who owns each one per platform, and when each is torn down.

## Review and testing

- [UI walk-through](./walk-through.md) — a route map of every screen, panel,
  menu, modal and transient surface, with the localization keys each one
  renders and a coverage table for all 122 key prefixes. Use it to review a
  translation in context, or to drive a layout pass under
  `?ui-language=max` (the hidden widest-translation pseudo-locale). The
  findings from the first such pass are
  [Max-locale layout findings](../plans/max-locale-findings.md).

## Related

- [Localization (i18n)](../i18n.md) — every user-visible string comes from the
  catalog; that document is the rule set and the enumerated exceptions.
- [Implementing features](../development/implementing-features.md) — the
  full-stack guide; its UI layer is the components document above.
- [Coding style](../CODING_STYLE.md) — the C#/TypeScript/CSS conventions the UI
  code follows.
