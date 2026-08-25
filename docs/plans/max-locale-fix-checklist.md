---
title: Max-locale fix checklist
description: Manual verification steps for each fix landed on feat/fix-ui-size-issues.
---

# Max-locale fix checklist

Verification steps for the fixes landing on `feat/fix-ui-size-issues`, one
section per root cause from
[Max-locale layout findings](./max-locale-findings.md).

Each item says where to go, what to do, what the bug looked like, and what
counts as a pass. Work top to bottom; the setup below is shared by all of them.

::: tip
Every fix must be checked **twice** — once in `max`, once in `en`. Max proves
the bug is gone; English proves nothing regressed for the language the layout
was originally tuned to.
:::

[[toc]]

## Setup

**Server** — `server-loop` must be running. If a fix was just landed, reload
with cache bypass (`Ctrl+Shift+R`), because the bundle URL is fingerprinted at
build time and served `immutable`.

**Entering Max**

| Do | Don't |
|---|---|
| `https://local.voxt.ai/chat/the-actual-one?ui-language=max` | `https://local.voxt.ai/?ui-language=max` |

The landing route redirects to `/chat` and **drops the query string** (finding
T1). The running instance stays in Max, but a refresh silently reverts to your
account language — so always navigate straight to a `/chat/...` URL, and if the
address bar has lost `?ui-language=max`, you are no longer testing Max.

Swap `max` for `en` in the same URL for the control pass.

**Viewports** — the audit used four. Resize the window for the three desktop
sizes; use DevTools device emulation (`Ctrl+Shift+M`) for the phone.

| Size | What it represents |
|---|---|
| 390 x 844 | iPhone 13 — smallest phone we support |
| 820 x 720 | narrowest layout that still counts as wide |
| 1280 x 800 | ordinary laptop |
| 1440 x 900 | large laptop |

Most items below reproduce at every size; where a size matters, it is called
out.

---

## C2 — Tab strips *(landed)*

Fixes **B1** (right-panel tabs unreachable) and **M4** (chat-list tab clipped
with no scroll affordance).

What changed: the right panel's `overflow-hidden` override is gone so it
scrolls like every other strip; all strips got a mask edge-fade on whichever
side has content scrolled off; and selecting a tab scrolls it into view.

### B1 — Right-panel tabs

1. Open `/chat/the-actual-one?ui-language=max` at **1440 x 900**.
2. Open the right panel (the toggle in the chat header).
3. You should see the tab strip: Учасники / Обговорення / Медіа / Файли /
   Посилання.

| | |
|---|---|
| **Before** | The strip needed 446px in a 351px panel. `overflow: hidden` meant no scrollbar, no wheel target, no gesture — **Посилання (Links) and its whole content pane could not be opened in any of the 22 languages that need more than 320px.** |
| **Pass** | A soft fade on the right edge shows there is more. Drag the strip left (or two-finger swipe) and Посилання comes into view. Click it — the Links pane opens. Click a tab that is partly off-screen and it scrolls itself fully into view. |

Repeat at **1280**, **820** and **390**. At 820 the *first* tab was also
clipped on the left — check the fade appears on the left once you have
scrolled.

### M4 — Chat-list tabs

1. `/chat?ui-language=max` at **390 x 844**.
2. Look at the strip above the chat list: Wszystkie / Groups / People /
   スレッド.

| | |
|---|---|
| **Before** | 374px of tabs in 328px. The strip did scroll, but carried `.no-scrollbar`, so there was no scrollbar and no hint a fourth tab existed — スレッド sat 30px past the right edge, invisible. |
| **Pass** | Right-edge fade is visible. Scrolling reveals スレッド; clicking it scrolls it fully into view. |

### C2 regression check — English

1. Same two surfaces at **1440 x 900**, `?ui-language=en`.

| | |
|---|---|
| **Pass** | **No fade at all**, on either strip. No scrollbar. The purple underline (the "bottom hill") sits exactly under the selected tab, and still does after you scroll a strip. |

English measured 320/320 and 289/289 — *exactly* zero headroom — so if a fade
appears in English, the strip has gained width it should not have.

---

## C6 — Left-panel title under the search box *(landed)*

Fixes **B4**. Title and collapsed search are now flex siblings in the header
row, so the title truncates *against* the search box instead of being painted
under it.

### B4 — The overlap

1. `/chat?ui-language=max` at **1440 x 900**. Look at the left panel header.

| | |
|---|---|
| **Before** | The collapsed search input was `panel width − 64px` (236px) and transparent, so its placeholder painted from x156 straight over the title. "Rechercher" sat on top of "Discussions" — 40px of overlap, at **every** viewport, in **both** themes, on **every** screen. |
| **Pass** | A clear gap between the title and the search pill (28–48px depending on width). Nothing overlaps. |

Repeat at **1280**, **820**, **390**.

### B4 — Related states

| Check | Pass |
|---|---|
| **Notifications** (unread group in the navbar) | Title gets the **full** header width — there is no search box here, so there must be no reserved empty space either. |
| **A place** (open any place) | Header must look exactly as it did before — the place variant was deliberately left alone. |
| **Long title** | Should ellipsise, never vanish. |
| **Open search**, then Escape | Expands and collapses over ~200ms with no jump. The title shrinks in lockstep and never overlaps the placeholder mid-animation. |
| **Ctrl+F**, and clicking outside | Still open and close the search. |
| **Filter badges** — open search, apply a filter | The results list must start *below* the badge row. It used to overlap it by 20px. |

### C6 regression check — English

| | |
|---|---|
| **Expected change** | The collapsed pill is **160px wide starting at x216**, where it used to be 236px starting at x140. This is inherent — the title's space has to come from somewhere, and the collapsed search needs a definite width for the open/close animation to work. Tunable in one place. |
| **Pass** | Everything else identical; say so if the narrower pill looks wrong to you. |

## Search-panel blink *(landed — not a Max finding)*

A separate defect, spotted during this work: clicking the search field made the
left panel blink.

**Cause** — two compounding problems. `background-image` is not an interpolable
CSS property, so the blue→violet wash slammed to full strength in a **single
frame** while the content behind it was still at opacity 0. And the outgoing
chat list was never animated at all, so the two lists cross-blended for 200ms.

**Fix** — the chat list and the search results are now the two layers of one
`ContentSwap`, wiping **down** on open and **up** on close. The tint travels
with the layer that carries it instead of being a backdrop racing the content.

### The transition

1. `/chat?ui-language=max`, any viewport. Click the search field. Then Escape.

| | |
|---|---|
| **Before** | A full-strength gradient appeared over the chat list in one frame, then everything else faded in behind it over 200ms. The corner rounded 200ms *after* that, on a delayed transition. |
| **Pass** | The search results sweep in downward from the top — where the search box you just clicked lives. No flash, no moment where two lists are both legible. Escape sweeps back upward. |

Check in **both themes** — the gradient is theme-sensitive.

### Related states

| Check | Pass |
|---|---|
| **First open of the session** (cold) vs a later one | Identical. The results must not appear empty and then fill in. |
| **A place** — open one, then its search | Same smooth fade. This variant used to blink too; it is fixed differently (its overlay must cover the place header, so it cannot be a swap layer). |
| **Filter badges** — open search, apply a filter | The results list starts *below* the badge row. The header grows to fit it. |
| **Chat-list scroll** — scroll down, open search, close it | Lands back on the chat you were looking at, not at the top. |

::: warning Known visual change
The blue wash no longer extends up behind the search-input row — it starts at
the top of the results, and the header row shows the darkened `bg-04` instead.
Say so if you wanted the header row tinted too.
:::

## C3 — Button labels *(landed)*

Fixes **M3**. The ellipsis was on `.btn`, but `.btn-content` is the real
clipping box — and it clipped with `justify-center`, so characters were lost
from **both** ends.

### M3 — The symmetric clip

1. `/settings?ui-language=max` at **1440 x 900**. Look at the tab rail.
2. Onboarding: `debugUI.resetOnboarding(true)`, reload, walk to the telemetry
   step and look at the footer button.

| | |
|---|---|
| **Before** | Labels cut at both ends with no ellipsis — the onboarding telemetry label overflowed its box by 19px on *each* side, so it read as neither the start nor the end of the word. `Settings_DeveloperTools` lost 5px off the right. |
| **Pass** | Nothing is cut on the left, ever. Long labels on compact buttons end in a real `…`; long labels on full-size buttons wrap to two lines and the button grows. |

### Which sizes wrap, which truncate

| Size | Behaviour |
|---|---|
| `btn-xs`, `btn-sm` | one line + `…` — row rhythm of toolbars and tab rails depends on it |
| default / `btn-md`, `btn-lg` | two lines, button grows |

### C3 regression check — English

| | |
|---|---|
| **Pass** | Buttons that already fit must be **pixel-identical**. 220 of 228 measured geometries were; the 8 that moved are all the "Voice & transcription" settings tab at 820px, which was cut by 13px before and now wraps (row 40 → 44px, rows below shift 4px). |

::: warning Known cosmetic issue
A wrapped label centres horizontally, because the UA stylesheet's
`button { text-align: center }` becomes visible once text wraps. Most obvious
in the settings tab rail, where a two-line label is centred while its one-line
neighbours are left-aligned. Left alone deliberately — fixing it belongs in
`settings-panel.css`, which the C4+C7 work owns.
:::

## C5 — Composed sentences *(landed)*

Fixes **B2** (blocker). The `_Prefix`/`_Suffix` pattern laid a sentence out as a
**flex row** with both literal parts pinned, so the dynamic value in the middle
absorbed 100% of any shortfall — down to zero.

Only 4 of the ~30 sites using this pattern had the flex shape; the rest were
already inline text and were left alone. A shared `.composed-sentence` utility
now lives in `src/nodejs/styles/tailwind.css`, next to `.flex-x`.

### B2 — The vanishing name

1. Open a peer chat whose welcome block is the contact-share variant, at
   **1280 x 800**, `?ui-language=max`.

| | |
|---|---|
| **Before** | prefix 213.6px / value **0px** / suffix 326.1px in a 502px row. Rendered as `Para compartilhar o contato de  さんの連絡先を共有するには、次を使いま` — the name gone, the sentence cut mid-word. At 390 the row overflowed to a 540px scrollWidth in a 352px box. |
| **Pass** | The name is present, and the sentence wraps to two lines at **word** boundaries. Nothing overflows. |

### The other three sites

| Where | How to reach it |
|---|---|
| Empty chat ("Say Hi to …") | a chat with no messages |
| Empty search results | a search that returns nothing |
| Recording sub-header | record in one chat, view another |
| Sign-in footer | sign out, open a peer chat URL |

### Spacing — the thing most likely to be wrong

The literal parts carry their own edge whitespace on purpose (`'s contact`
hugs the name; ` さんの` does not). Check a language that leads with a space
and one that doesn't.

| | |
|---|---|
| **Pass** | `Say "Hi" to Jalāl…!` — one space, not two. Punctuation hugs the name where the catalog has no space. |

Whitespace-only nodes between markup tags are discarded in a flex row but
**render** in inline layout, so a stray newline in the `.razor` becomes a
visible space. This was caught and fixed at all four sites — it is the most
likely place for a regression if these files are edited later.

### C5 regression check — English

| | |
|---|---|
| **Pass** | Byte-identical geometry at 1280 and 1440 for all three inline sites. At 390/820 the sign-in footer previously *clipped* a long name and now wraps to show it in full — an improvement, not a regression. |

## C4 + C7 — Settings rows *(landed)*

Fixes **B3** (blocker), **M9** and **M6**, plus the centring fallout from the
button fix. All in `settings-panel.css`.

### B3 — Radio options were indistinguishable

1. `/settings?ui-language=max` at **1440 x 900** → Voice & transcription →
   Transcription engines.

| | |
|---|---|
| **Before** | Labels overflowed by 133–240px, and the distinguishing token is the *trailing* one (`… через OpenAI` vs `… через Gemini`) — exactly what the ellipsis ate. Two options rendered as literally the same text. |
| **Pass** | Every option reads differently. Labels take up to two lines; **row heights do not change** (the clamp caps at the row's existing min-height). |

Only rows carrying a radio or checkbox were unlocked — every other settings
row keeps its one-line rhythm.

### M9 — Value overhanging its row

1. Chat settings → "Chat type" (needs a chat you own), `?ui-language=max`.

| | |
|---|---|
| **Before** | `Herkese açık` wrapped to two lines inside a fixed 24px slot and spilled **8px above and 8px below**, so it no longer aligned with its label. |
| **Pass** | No spill. Row stays 48px, label position unchanged. |

### M6 — Settings header wider than the phone

1. `/settings?ui-language=max` at **390 x 844**, any tab.

| | |
|---|---|
| **Before** | The header measured **422 x 57 in a 390px viewport** — `box-sizing: content-box` (used so the safe-area padding would add to the *height*) made the horizontal `px-4` add to the width too. Title ran 16px past the right edge. Not Max-specific: 422px in English too. |
| **Pass** | 390 x 56, title right edge inside the viewport. |

Height goes 57 → 56 because the 1px bottom border is now inside the box — the
one intentional pixel change.

### Tab rail alignment

| | |
|---|---|
| **Before** | Once the button fix let labels wrap, a two-line rail label centred itself (UA `button { text-align: center }`) while its one-line neighbours stayed left-aligned. |
| **Pass** | Every line of every rail label starts at the same x. Modal buttons stay centred. |

### C4+C7 regression check — English

| | |
|---|---|
| **Pass** | 5 of 28 rows move: 3 option rows that regain a previously-ellipsised `(optional)` tail, and 2 rows whose right slot holds an icon taller than 24px that used to spill out of the fixed box. **No row height changed anywhere**, in any tab, at any viewport. |

---

## Deferred to the final sweep

Per-item verification is deliberately shallow from C8 onward: the finding's own
surface at 390x844 and 1440x900 in `max`, plus a quick English sanity look on
that same surface. Reaching a surface (onboarding, a place, a live call) costs
far more than measuring it, so the breadth is paid **once**, at the end, instead
of once per fix.

::: tip English is the main event, not a control
English is the primary product language, so the final pass is a **full English
walk of the whole UI** — the quality gate. Max is the stress test that finds
the bugs; English is what has to actually look right. The deferred Max checks
ride along on the same arrangement, since reaching a surface is the expensive
part and both locales can be measured once you are there.
:::

**Shape of the final pass.** 4-6 agents in parallel, read-only — no file edits,
no rebundle, no server restart. They drive **Playwright over CDP** rather than
the chrome MCP: the MCP keeps one "selected page" per connection, so two agents
sharing it would steal each other's tab mid-measurement. chrome1 (`:9222`) and
chrome2 (`:9223`) hold two different signed-in accounts, which is what makes the
two-user surfaces reachable. Partition by surface so no two agents need the same
arrangement.

What the final sweep must cover:

| Item | Deferred |
|---|---|
| **all** | **a full English walk of every surface below — the primary-language gate** |
| all | 820x720 and 1280x800 — dropped per-item; they have been interpolations between 390 and 1440 every time |
| all | both themes (`theme-dark` / `theme-light`) |
| C1 | `ListeningTimerBubble`, `RecordButtonBubble2_Disabled`; onboarding `PhoneStep`, `EmailStep`, `TimeZoneStep`, `DataCollectionStep`, places + transcription tutorials |
| C5 | `EmptySearchChatContent`; `ChatFooter` sign-in footer on the real surface (guest peer chat) rather than injected markup |
| C4+C7 | a long chat list, where the `initialIndex` load-window re-centering actually engages |
| blink | scroll restore on a desktop-height list; `EnableIncompleteUI` TabPanel branch |
| banners | every banner that could not be rendered in its own right |

::: tip Why English stays in the per-item pass
It is the one breadth dimension that has caught a real regression — the button
fix shifted the account avatar +8px in **English**, which no Max measurement
would have shown.
:::

## What the parallel sweep found

The deferred pass above was run: five read-only agents, one surface each,
driving Playwright over CDP rather than the chrome MCP (which keeps one
selected page per connection, so agents sharing it steal each other's tabs).

Most fixes held. The ones that did not are fixed, and the full account is in
[the findings doc](./max-locale-findings.md#what-the-sweep-changed). The
short version, because these are the ones worth re-checking by hand:

| | |
|---|---|
| **Tab strips** | One button rule broke all of them three ways, and silently disabled the scroller added for B1/M4. Check labels are one line, CJK is not stacked, and English shows no fade when the tabs genuinely fit. |
| **Settings &rarr; Documents** | Got *worse* before it got better (447 &rarr; 648px in a 390px viewport). Check the header is &le;390 and the sub-tab strip scrolls. |
| **Status badge** | Needed a 56px floor, not 48 &mdash; Chrome paints a character before the ellipsis. Check a squeezed badge shows all three dots. |
| **Banners** | The container query fires against the *content box*, so it was costing English a row on every phone. Check English stays inline from 350px up and Max still wraps. |
| **Bubbles** | Safe areas, a `max-width` that was not a cap, and a counter that absorbed every shortfall. |
| **Modal titles** | Two headers exist; the narrow one was missed on the first pass. |

::: tip Themes are out of scope for layout
Every theme-scoped rule in this codebase sets only colour variables &mdash;
verified across all eight files that mention `theme-dark`/`theme-light`. Themes
cannot change sizes, so they are worth checking only where colour matters (the
search tint, the banner gradients).
:::

## Reporting a problem

If an item fails, the useful detail is: which finding, which viewport, which
language, and what you saw instead. A screenshot of the failing box beats a
description — most of these are geometry bugs.
