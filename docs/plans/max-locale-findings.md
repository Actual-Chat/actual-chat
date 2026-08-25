---
title: Max-locale layout findings
description: Layout defects found by walking the UI with ?ui-language=max, with proposed fixes.
---

# Max-locale layout findings

Result of the first full [UI walk-through](../ui/walk-through.md) under
`?ui-language=max`, at 390x844 (iPhone 13), 820x720 (narrowest wide layout),
1280x800 and 1440x900.

Every finding below was verified against `?ui-language=en` on the same screen
at the same size, so "Max-caused" means English fits and Max does not.
Nothing here is fixed &mdash; this is the list.

::: info How to read this
Findings are grouped into **eight root causes**. Fixing the root cause fixes
every instance under it, which is why the fix lives on the category and the
instances only carry evidence. Each category has a **primary** fix and, where
there is a real alternative, a second option.
:::

[[toc]]

## Summary

| # | Finding | Severity | Category | Viewports |
|---|---|---|---|---|
| B1 | Right-panel tabs clipped with no way to reach them | Blocker | [C2](#c2-tab-strips-have-no-overflow-strategy) | all |
| B2 | Composed sentence drops its dynamic middle entirely | Blocker | [C5](#c5-composed-sentences-annihilate-their-dynamic-middle) | &le;1280 |
| B3 | Radio options become indistinguishable after truncation | Blocker | [C4](#c4-one-line-ellipsis-on-text-that-distinguishes-things) | all |
| B4 | Left-panel title sits under the search box | Blocker | [C6](#c6-an-always-on-overlay-covers-a-static-sibling) | all |
| M2 | Bubble title and buttons escape the bubble | Major | [C1](#c1-fixed-width-nowrap-containers) | all |
| M3 | Button labels hard-clipped on both ends | Major | [C3](#c3-buttons-hard-clip-their-own-label) | all |
| M4 | Chat-list tab clipped, no scroll affordance | Major | [C2](#c2-tab-strips-have-no-overflow-strategy) | &le;1280 |
| M5 | Chat title loses all its width to a status badge | Major | [C8](#c8-decoration-outranks-identity-in-flex-rows) | &le;1440 |
| M6 | Settings tab header overflows the phone viewport | Major | [C7](#c7-desktop-sized-boxes-leak-into-the-phone-layout) | 390 |
| M7 | Onboarding step content escapes its card | Major | [C1](#c1-fixed-width-nowrap-containers) | 390 |
| M8 | Modal titles truncate to one ellipsised line | Major | [C4](#c4-one-line-ellipsis-on-text-that-distinguishes-things) | all |
| M9 | Settings row value wraps out of its 24 px slot | Major | [C7](#c7-desktop-sized-boxes-leak-into-the-phone-layout) | all |
| M10 | "Join muted" &mdash; the call CTA &mdash; cut mid-word | Major | [C10](#c10-chrome-that-shrinks-for-a-sibling-clips-instead-of-ellipsising) | all |
| M11 | Chat header subtitle clipped once a call starts | Major | [C10](#c10-chrome-that-shrinks-for-a-sibling-clips-instead-of-ellipsising) | all |
| m1 | Message menu positioned above the viewport top | Minor | [C9](#c9-floating-elements-are-not-clamped-to-the-viewport) | short viewports |
| m2 | Account name squeezed to four characters | Minor | [C8](#c8-decoration-outranks-identity-in-flex-rows) | desktop |
| L1 | 8 hardcoded English strings in `JoinVideoCallModal` | l10n gap | [L](#l1--the-video-call-modal-is-not-localized-at-all) | all |
| T1 | `?ui-language=max` dropped by the landing redirect | Tooling | &mdash; | all |
| T2 | `Place_TabMedia/Files/Links` are dead keys | Hygiene | &mdash; | &mdash; |

**The shape of the problem.** Max is 1.62x wider than English at the median and
2.37x at p90, but the failures are not spread evenly. They cluster where a
layout was sized to a *specific English string* rather than to a range:
short labels grow the most in relative terms (`All` &rarr; `Wszystkie` is
3.97x, `Off` &rarr; `Desactivado` is 3.98x), and those short labels are
precisely what tab strips, badges and compact buttons contain.

Two of the failing containers fit English with **exactly zero headroom** &mdash;
both tab strips measured `scrollWidth === clientWidth` to the pixel. That is a
sign the widths were tuned to the English text.

---

## C1. Fixed-width, nowrap containers

A box with a hard `max-width` and `white-space: nowrap` cannot respond to a
longer string at all: the text simply leaves the box.

### M2 &mdash; Bubble title and button row escape the bubble

**Where** Every feature bubble.
[`bubble.css:8`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/Bubble/bubble.css#L8),
[`BubbleContent.razor`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/Bubble/BubbleContent.razor).

**Evidence** At 390x844 the "Your account and settings" bubble
(`Bubble_ManageAccountTitle`, 1.45x) renders a 265 px title inside a 224 px
content box with `overflow: visible` &mdash; **41 px of text is painted outside
the rounded purple background**. On the same bubble the footer row
(`Common_Skip` 3.27x + `Common_Next` 2.27x = "Überspringen" + "Berikutnya")
overflows by 8 px at 390 and by ~40 px at 1280, so the **Next button lands
outside the bubble**, over the member list.

**Root cause**

```css
.ac-bubble {
    @apply min-w-64 max-w-64;      /* hard 256 px */
    @apply rounded-lg whitespace-nowrap;
}
```

`.bubble-body` resets `white-space: normal`; `.bubble-title` and the footer row
do not, and nothing clips or wraps.

**Primary fix** Stop making nowrap the bubble-wide default and let the box grow
a little:

```css
.ac-bubble {
    @apply min-w-64;
    max-width: min(20rem, calc(100vw - 2rem));
    /* drop whitespace-nowrap here; opt in where a single line is required */
}
.bubble-title { @apply whitespace-normal; }
.bubble-buttons { @apply flex-wrap; }
```

**Alternative** Keep the 256 px width (it is a deliberate visual constant) and
only fix the two children: `whitespace-normal` on `.bubble-title`, and
`flex-wrap` + `gap-y-1` on `.bubble-buttons` so the second button drops to its
own line instead of leaving the box.

### M7 &mdash; Onboarding step content escapes its card

**Where** [`Components/Onboarding/`](https://github.com/Actual-Chat/actual-chat/tree/main/src/dotnet/UI.Blazor.App/Components/Onboarding), at 390x844.

**Evidence**
- Conversation-summaries step: the title `p` is 320 px wide inside a 300 px
  `.stepper-content` with `overflow: visible` &mdash; 20 px sticks out.
- Avatar step: `.stepper-content` overflows by 40 px; the
  "Сгенерировать аватар" button ends 5 px past the right edge of the viewport.

**Primary fix** `min-w-0` on `.stepper-content` and its flex children, plus
`overflow-wrap: anywhere` on step titles, and `flex-wrap` on the avatar step's
button row.

**Alternative** Step down the step-title type scale below `md` and give the
avatar buttons `w-full` on narrow screens, so they stack rather than compete.

---

## C2. Tab strips have no overflow strategy

Both tab strips size themselves to their content and then either hide the
overflow or scroll it without telling anyone.

### B1 &mdash; Right-panel tabs are unreachable *(blocker)*

**Where** Right panel &rarr; Members / Threads / Media / Files / Links.
[`tab-panel.css:108`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/TabPanel/tab-panel.css#L108).

**Evidence**

| Width | English | Max | Result |
|---|---|---|---|
| 1440 | 320 / 320 px &mdash; **zero headroom** | 446 / 351 px | "Посилання" (Links) 87 px off-panel |
| 1280 | fits | 446 / 351 px | Links off-panel |
| 820 | fits | 446 / 409 px | Links clipped, *and* the first tab clipped on the left |
| 390 | fits | 446 / 389 px | Links clipped |

`RightPanel_TabLinks` alone grows 2.27x (`Links` &rarr; `Посилання`);
`TabMedia` 1.98x, `TabFiles` 2.05x. Combined, the strip needs 39% more room
than it has.

**Root cause** The generic strip is scrollable, but the right panel overrides it:

```css
.tab-panel-tabs .btn-group { @apply overflow-y-hidden overflow-x-auto; }

.side-nav.side-nav-right .tab-panel-tabs .btn-group {
    @apply justify-start;
    @apply overflow-hidden;      /* <- kills the only escape hatch */
}
```

There is no scrollbar, no wheel target, no gesture. The Links tab and its whole
content pane cannot be opened in any of the 22 languages that need more than
320 px.

**Primary fix** Delete the `overflow-hidden` override so the right panel
inherits `overflow-x: auto`, then add a real affordance to
`.tab-panel-tabs .btn-group`: an edge fade (`mask-image` on the scrolled side)
and `scroll-behavior: smooth` with the selected tab scrolled into view on
change.

**Alternative** Change the right-panel strip to overflow into a menu: keep the
tabs that fit, collapse the rest behind a trailing **...** button. Costs a
menu but guarantees reachability at every width and in every language, which
edge fades do not (a fade still needs a scroll gesture a mouse user may not
attempt).

### M4 &mdash; Chat-list tab clipped, no scroll affordance

**Where** Left panel &rarr; All / Groups / People / Threads.

**Evidence** English fits with **zero headroom** (289 / 289 px). Max needs
374 px. At 390x844 the strip overflows by 46 px and the "スレッド" (Threads)
tab sits 30 px past the right edge. The strip *is* `overflow-x: auto`, but it
also carries `.no-scrollbar`, so on a desktop pointer there is nothing to grab
and no hint that a fourth tab exists.

`ChatList_TabAll` is the single worst short label in the catalog: `All` &rarr;
`Wszystkie`, 3.97x.

**Primary fix** Same as B1 &mdash; edge fade plus scroll-selected-into-view.
Because this strip already scrolls, the affordance is the whole fix.

**Alternative** Let the strip wrap to two rows below the `md` breakpoint
(`flex-wrap` + `justify-center`). Costs 40 px of vertical space on phones but
removes the failure mode entirely.

---

## C3. Buttons hard-clip their own label

### M3 &mdash; Labels cut on both ends, no ellipsis

**Where** Every `Button` in the app.
[`button.css:24-48`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/Button/button.css#L24-L48).

**Evidence**

| Instance | Label | Clipped by |
|---|---|---|
| Settings tab rail | `Settings_DeveloperTools` &mdash; "Инструменти за разработчици" (2.03x) | 5 px |
| Onboarding footer, telemetry step | `Onboarding_EnableTelemetry` &mdash; "テレメトリを有効にする" (1.47x) | 27 px, symmetric |

Measured on the onboarding button: the label span starts 27 px *to the left of*
its clipping box and ends 27 px to the right &mdash; characters are lost from
**both** ends, so the label reads as neither the start nor the end of the word.

**Root cause** The ellipsis is on the wrong element:

```css
.btn, .btn.btn-md {
    @apply truncate;              /* overflow:hidden + ellipsis + nowrap */
}
.btn > .btn-content {
    @apply flex-x items-center justify-center gap-x-2;
    @apply overflow-hidden;       /* no text-overflow -> clip */
}
```

The label lives inside `.btn-content`, so `.btn-content` is the clipping box.
It clips rather than ellipsises, and because it is `justify-center` the clip is
symmetric.

**Primary fix** Move the truncation to where the clipping actually happens:

```css
.btn > .btn-content {
    @apply overflow-hidden;
    @apply min-w-0;
}
.btn > .btn-content > span { @apply truncate; }
```

`min-w-0` lets the label shrink instead of pushing the box, and the ellipsis
then appears at the end where a reader expects it.

**Alternative** Let large buttons wrap. For `btn-lg` / `btn-modal` / the
onboarding footer, `white-space: normal` + `line-clamp-2` + `h-auto` shows the
whole label on two lines; keep single-line truncation only for compact buttons
(`btn-xs`, `btn-sm`) where two lines would break the row rhythm. This is the
better answer for primary actions, where losing characters is never acceptable.

---

## C4. One-line ellipsis on text that distinguishes things

Truncation is fine for a chat title &mdash; the avatar and position already
identify it. It is not fine when the truncated tail is the *only* thing that
tells two rows apart.

### B3 &mdash; Radio options become indistinguishable *(blocker)*

**Where** Settings &rarr; Voice &amp; transcription &rarr; Transcription
engines.
[`settings-panel.css:185`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/SettingsPanel/settings-panel.css#L185).

**Evidence** At both 1280 and 1440 the option list renders:

```
( ) Soniox, без повторной транскрип...
( ) Soniox + повторная транскрипция...
( ) Soniox + повторная транскрипция...     <- same text, different option
( ) Deepgram + повторная транскрип...
```

Labels overflow by 133-240 px. The template is
`Transcription_Retranscription_Format` = `"{0} + повторная транскрипция через {1}"`,
so the **distinguishing token is the trailing `{1}`** &mdash; exactly what the
ellipsis eats. Two radio options are literally identical on screen.

**Root cause**

```css
.tile-item-body-content {
    @apply w-full min-h-5 max-h-5;   /* one 20 px line, hard */
    @apply truncate;
}
```

Every settings row label is locked to one line, everywhere.

**Primary fix** Let option rows grow. Inside a `form-block` that contains
radios or checkboxes, drop the height lock and allow two lines:

```css
.form-block .tile-item-body-content {
    @apply max-h-none h-auto;
    @apply whitespace-normal line-clamp-2;
    @apply overflow-hidden;
}
.form-block .tile-item { @apply items-start; }
```

**Alternative** Keep one line but truncate in the middle
(`Soniox + повторная… через OpenAI`). This preserves both ends and therefore
the distinguishing token, and it keeps the row rhythm. It needs a small helper
since CSS has no middle ellipsis &mdash; and it reads worse than two lines,
so prefer it only where vertical space is genuinely scarce.

**Other instances of the same rule.** Place settings truncates its
copy-chat action to "Sao chép cuộc trò chuyện vào Địa điể…"; the chat-settings
row set behaves the same. Those are tolerable (the row is still identifiable),
which is why the fix should be scoped to `form-block` option lists first rather
than applied to every `TileItem` at once.

### M8 &mdash; Modal titles truncate to one ellipsised line

**Where** Every modal.
[`modal.css:191`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/Modal/modal.css#L191).

**Evidence** The mic-permission guide renders
"Rozwiązywanie problemów z nagry…" at 1280x800 in a 384 px modal. The title is
the only thing telling you which of eight guides you opened.

**Primary fix** `line-clamp-2` on `.modal-header .modal-title` and a `min-h`
instead of a fixed `ordinary-header` height, so a two-line title grows the
header rather than being cut.

**Alternative** Keep one line and shrink the type when it overflows. Cheap to
express with `text-wrap: balance` plus a smaller `md:` size, but it only buys
one step of headroom and does not survive the 4x tail of the distribution.

---

## C5. Composed sentences annihilate their dynamic middle

The `_Prefix` / `_Suffix` pattern builds a sentence as
`[prefix][dynamic value][suffix]` in a flex row. Both literal parts are pinned;
the dynamic value is the only shrinkable item, so it absorbs 100% of any
shortfall &mdash; down to zero.

### B2 &mdash; The contact name disappears *(blocker)*

**Where**
[`ChatWelcomeBlock.razor:26-30`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatWelcomeBlock.razor#L26-L30),
[`chat-view.css:468-478`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/ChatView/chat-view.css#L468-L478).

**Evidence** At 1280x800, in a 502 px row:

| element | width |
|---|---|
| `.c-incut` (prefix) | 214 px, `flex-none` |
| `.c-contact` (**the contact's name**) | **0 px** |
| `.c-incut` (suffix) | 326 px, `flex-none`, itself clipped |

The line renders as
`Para compartilhar o contato de  さんの連絡先を共有するには、次を使いま` &mdash;
the name is gone and the sentence is cut mid-word. `ChatWelcome_ShareContact`
grows 3.3x.

**Root cause**

```css
.chat-welcome-block .c-content.c-contact-share > .c-incut {
    @apply flex-none;             /* cannot shrink */
    @apply whitespace-pre;        /* cannot wrap */
}
.chat-welcome-block .c-content > .c-contact {
    @apply max-w-100 truncate;    /* the only flexible item */
}
```

**Blast radius** 30 composed-sentence sites use this shape. The ones under most
pressure:

| Site | Growth | Component |
|---|---|---|
| `ChatView_SayHiTo` | 3.8x | `EmptyChatContent`, `EmptySearchChatContent` |
| `DeleteAccount_Warning` | 3.4x | `DeleteAccountModal` |
| `ChatWelcome_ShareContact` | 3.3x | `ChatWelcomeBlock` |
| `Alert_ConfirmUnread` | 3.0x | `NotifyAllButton` |
| `ClientUpgrade_Version` | 2.7x | `ClientUpgradeCover` |
| `Banner_LockScreenCalls` | 2.7x | `FullScreenCallsDisabledBanner` |
| `Banner_ImportContacts` | 2.5x | `ContactsPermissionBanner` |
| `Presence_LastSeen` | 2.4x | `PresenceFragments` |
| `ChatFooter_ToChatWith` | 2.1x | `ChatFooter` |
| `Banner_Notify` | 2.1x | `NotificationsPermissionBanner` |

Individual suffixes are far worse: `Photo_Intro_Suffix` is 82x
(`"."` &rarr; `"「設定」で有効にしてください。"`),
`ClientUpgrade_Version_Suffix` 62x, `VoiceSettings_SecondLanguageHint_Suffix`
60x, `Alert_ConfirmUnread_Suffix` 51x. Any language whose grammar moves the
verb after the name turns a one-character suffix into a clause.

**Primary fix** Stop laying a sentence out with flexbox. Make it real inline
text and let it wrap:

```css
.c-contact-share {
    display: block;
    text-align: center;
    overflow-wrap: anywhere;
}
.c-contact-share > .c-incut { display: inline; white-space: pre-wrap; }
.c-contact-share > .c-contact {
    display: inline-block;
    max-width: 100%;
    vertical-align: bottom;
    @apply truncate;
}
```

The sentence then wraps to two lines and the name truncates only when it is
itself longer than the line &mdash; which is the behaviour the design intends.
This should be applied as a **shared utility class** (e.g.
`.composed-sentence` in `UI.Blazor`) and used by all 30 sites, not fixed
one file at a time.

**Alternative** Keep flexbox and add `flex-wrap: wrap` to the row plus a floor
on the dynamic part (`min-width: 6ch`). Smaller diff, but it wraps at part
boundaries rather than word boundaries, so a long prefix still produces an
ugly ragged break. Prefer it only where the row must stay a flex container for
other reasons (e.g. an inline avatar).

---

## C6. An always-on overlay covers a static sibling

Two pieces of text painted on top of each other is the most visible failure
mode there is: the reader gets neither string, and it does not look like a
translation problem &mdash; it looks like the app is broken.

### B4 &mdash; Left-panel title sits under the search box *(blocker)*

**Where** Left panel header &mdash; visible on **every screen**, at **every**
viewport tested, in **both** themes, whether a chat, a place or settings is
open. It is the single most-seen defect in this list.

Source:
[`left-panel.css:121-132`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/LeftPanel/left-panel.css#L121-L132),
[`LeftPanelChatContentHeader.razor`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/LeftPanel/LeftPanelChatContentHeader.razor).

**Evidence** At every width tested, `Navbar_Chats` ("Chats" &rarr;
"Discussions", 1.99x) renders under the collapsed search field:

| | box | text |
|---|---|---|
| `.c-title` | x 76, w 300 | "Discussions" ends at ~x 172 |
| search input | x 140, w 236 | starts at x 140 |

32 x 23 px of the title is painted under the search box. In English the title
ends at ~x 118 and never reaches it &mdash; which is why this has never been
seen.

**Root cause** The title is a normal flex child of the header, while the search
panel is `position: absolute; z-index: 50` and spans the **full** header width
even in its collapsed state. Nothing reserves space for it:

```css
.left-panel-content-header > .c-content > .c-title {
    @apply flex-1 truncate pl-1;
}
.left-panel-content-header .c-ending {
    @apply absolute right-0;
    @apply w-full;               /* covers the title */
}
```

**Scope** Only the chat-list header variant
(`LeftPanelChatContentHeader`). The place variant
(`.left-panel-content-header.place`, `LeftPanelPlaceContentHeader`) lays its
title out in a `.c-info` grid below the icon and does **not** collide &mdash;
so the fix belongs in the chat variant, and the place variant is the shape to
copy.

**Primary fix** Make the header a real flex row in the collapsed state: title
`flex-1 min-w-0 truncate`, search `flex-none` at its collapsed width, and drop
the absolute positioning until the search panel actually opens. The title then
truncates against the search box instead of disappearing under it.

**Alternative** Keep the overlay and reserve its space:
`padding-right: <collapsed search width>` on `.c-content`, so `.c-title`'s
flex basis never reaches the covered region. One line, but the constant has to
be kept in sync with the search box, and it wastes the space when the search
box is hidden.

---

## C7. Desktop-sized boxes leak into the phone layout

### M6 &mdash; Settings tab header overflows the phone viewport

**Where** `/settings` at 390x844, every tab.

**Evidence** `.settings-tab-header` measures **422 px inside a 390 px
viewport**; its `.c-title` runs from x 56 to x 406, i.e. 16 px past the right
edge. The header inherits the desktop settings-column width instead of the
viewport. In English the titles are short enough that nothing visibly falls off
the edge, so the structural overflow is invisible.

**Primary fix** Constrain the panel to the viewport on narrow layouts &mdash;
`w-full max-w-full` on `.settings-panel` / `.settings-tab`, and `min-w-0` down
the flex chain so the title box can shrink.

**Alternative** `overflow-x: hidden` on `.settings-panel`. Stops the leak but
hides the symptom rather than fixing the width, and long titles then get cut
with no ellipsis.

### M9 &mdash; Settings row value wraps out of its 24 px slot

**Where** Chat settings &rarr; "Chat type"; the same slot is used by every
`TileItem` right side.
[`settings-panel.css:193`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor/Components/SettingsPanel/settings-panel.css#L193).

**Evidence** `Chat_Public` ("Public" &rarr; "Herkese açık", 2.04x) wraps to two
lines inside `.tile-item-right { h-6 }` (24 px), so the value overhangs the row
and no longer aligns with its label.

**Primary fix** `h-auto min-h-6` + `items-start` on `.tile-item-right`, and let
the row grow with its content.

**Alternative** Below `md`, move a long value onto its own line under the
label (`.tile-item` becomes `flex-y`). Better on phones, but it changes the
visual rhythm of the whole settings list, so it is the bigger change.

---

## C8. Decoration outranks identity in flex rows

When a decorative element is `white-space: nowrap` and the identity text is
`truncate`, the identity text pays for the whole shortfall.

### M5 &mdash; Chat title loses all its width to the status badge

**Where** Right-panel header.
[`status-badge.css:1-8`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/StatusBadge/status-badge.css#L1-L8),
`ChatSidePanelHeader.razor`.

**Evidence** At 1280x800, in a 319 px row:

| | English | Max |
|---|---|---|
| `.c-title` ("The Actual One") | 212 px, not truncated | **120 px, ellipsised** (needs 135) |
| `.status-badge` (`Chat_PublicChat`, 2.41x) | 99 px | 191 px, untouched |

The chat's name &mdash; the one thing that says which chat this panel is about
&mdash; is cut, while a decorative "public chat" badge keeps every pixel it
asked for. At 820 the title renders as "The Actual …".

### m2 &mdash; Account name squeezed to four characters

Same shape, settings footer card: `Common_Share` ("Share" &rarr;
"Compartilhar", 2.25x) grows the button and the account name collapses to
"Naval Pu…" on the 320 px desktop rail. On the 390 px phone layout the rail is
wider and the name survives &mdash; the desktop is the broken case.

**Primary fix** Give the flex row an explicit shrink order:

```css
.c-title      { flex: 1 1 auto; min-width: 6rem; @apply truncate; }
.status-badge { flex: 0 1 auto; min-width: 0; @apply truncate; }
```

Identity keeps a floor; decoration truncates first.

**Alternative** Collapse the badge to its icon below a width threshold, with
the text moved to a tooltip / `aria-label`. Guarantees the title's full width
and reads cleanly, at the cost of a discoverability loss for the badge.

---

## C9. Floating elements are not clamped to the viewport

### m1 &mdash; Message menu is positioned above the viewport top

**Where** Message context menu, `menu-host.ts` positioning.

**Evidence** At 1280x800 the Max message menu measures 347 x **427** px and is
placed at y = **-27**: its first item is off-screen. The English menu at the
same anchor is 262 x 387 px and fits. The menu is anchored bottom-to-cursor and
the positioner does not clamp the top edge.

This is height-driven, so it is **not purely a Max bug** &mdash; the same menu
overflows in English on a short enough viewport. Max makes it 40 px taller and
therefore reproducible on a normal laptop.

**Primary fix** Clamp the computed position to the viewport with padding (a
`shift`-style middleware step after the flip), so the menu never starts above
`0 + safe-area`.

**Alternative** Cap the menu at `max-height: calc(100vh - 2rem)` and let it
scroll internally. Robust for any item count, but a scrolling context menu is a
worse interaction, so prefer clamping and use the cap only as the backstop for
very long menus.

---

## C10. Chrome that shrinks for a sibling clips instead of ellipsising

The chat header is sized for its resting state. When a call starts, the
activity panel and the call controls take part of that row, and the text that
was already tight is cut &mdash; with `text-overflow: clip`, so it loses
characters rather than gaining an ellipsis.

Both of these need a **live two-user session** to see, which is why they
survived every earlier pass.

### M10 &mdash; The "Join muted" call-to-action is cut mid-word

**Where** Chat header, on the side of a user who has *not* joined a call that
is already running. `.chat-activity-panel.video-streaming.not-participating`.

**Evidence** With user A recording and streaming video, user B's header renders
`Call_JoinMuted` &mdash; "Gabung dalam keadaan bisu" &mdash; as
**"Gabung dalam keadaan bi"**. The label overflows by **41 px** and its
`.c-buttons` container is clipped a further 35 px by the parent. This is the
primary control for joining a live call.

**Primary fix** Give the activity panel a real shrink budget: `min-w-0` on the
panel and its button container, `truncate` on the label, and a floor
(`min-width`) so the pill never shrinks below a readable width &mdash; the chat
title should yield first, since it is repeated in the panel behind.

**Alternative** Below a width threshold, reduce the CTA to its icon plus a
tooltip and move the label into the activity panel's own row. Keeps the full
wording, at the cost of a less obvious affordance.

### M11 &mdash; Chat header subtitle clipped once a call starts

**Where** `.chat-header-title`, whenever the layout carries
`has-listening-activity` or `has-open-video-panel`.

**Evidence** At 1280x800 the title block drops from 544 px to 174 px while its
content needs 180 px. Computed `text-overflow: clip`, so
"2 人がオンライン | メンバー 47 人" renders as
"2 人がオンライン | メンバー 47 ノ" &mdash; the final glyph is sliced in half
rather than replaced with an ellipsis.

**Primary fix** `min-w-0` + `truncate` on `.chat-header-title > .c-info` so the
subtitle ellipsises. One line, and it makes every other header state safe too.

**Alternative** Drop the member count from the subtitle while a call is active
&mdash; the participant list is already on screen in the activity panel, so the
information is redundant exactly when the room runs out.

---

## Localization gaps

Not layout &mdash; but the Max walk is what surfaced them, because untranslated
strings stand out immediately when everything around them is Max.

### L1 &mdash; The video-call modal is not localized at all

**Where**
[`JoinVideoCallModal.razor:196-201, 581-584`](https://github.com/Actual-Chat/actual-chat/blob/main/src/dotnet/UI.Blazor.App/Components/JoinVideoCallModal/JoinVideoCallModal.razor#L196-L201).

Eight user-visible strings are hardcoded English:

| Line | String |
|---|---|
| 196 | `"Video Settings"` |
| 197 | `"Join the video"` |
| 198 | `"Video preview"` |
| 199 | `"Apply"` |
| 200 | `"Join"` |
| 201 | `"Start video"` |
| 581 | `"Camera is off"` |
| 584 | `"Camera is unavailable"` |

This breaks the project's one hard rule ("never hardcode user-visible English
text in UI code") and two of them **already have catalog keys**:
`Video_StartVideo` is literally `"Start video"`, and `Common_Join` is `"Join"`.

**Why the tests missed it.** `ui-localization-smoke.test.ts` asserts that no
visible text equals an English catalog value whose translation differs. Six of
the eight are not catalog values at all, so there is nothing to match; the two
that are would be caught &mdash; if the smoke test ever opened this modal. It
does not, because the modal needs a live video session.

**Primary fix** Add the six missing strings to `Strings.en.json` under
`Video_*` / `Call_*`, reuse `Video_StartVideo` and `Common_Join` for the other
two, and regenerate the derived catalogs
(`scripts/derive-bcms.cmd` then `scripts/derive-max.cmd`).

**Alternative** None &mdash; this is not a judgement call. The modal is
ordinary UI chrome, not a diagnostics panel, so no documented exception covers
it.

---

## Tooling and hygiene

### T1 &mdash; The Max override is dropped by the landing redirect

Opening `/?ui-language=max` while signed in redirects to `/chat` **without the
query string**. The running instance stays in Max (the TypeScript bootstrap
read the parameter before the redirect fired), but the address bar no longer
carries it, so a refresh silently reverts to the account language &mdash; and
anyone testing assumes the reload is still Max.

**Primary fix** Preserve the query string across the landing redirect.

**Alternative** Document `/chat?ui-language=max` as the entry point. Already
done in the [walk-through](../ui/walk-through.md#the-max-pseudo-locale), but it
leaves the trap in place for everyone else.

### T2 &mdash; Dead keys

`Place_TabMedia`, `Place_TabFiles`, `Place_TabLinks` have no call site &mdash;
the place panel renders `RightPanel_Tab*`. They are translated into 22
languages and shown to nobody. Delete them, or wire them up if the place panel
is meant to have its own labels.

---

## Not defects

Recorded so they are not re-filed:

- **English text under Max.** Max holds the *widest* of 22 translations, and for
  some keys that is English (`Notifications panel`, `Transcription_Languages`).
- **Landing, legal docs, `/test/*` pages, diagnostics panels.** Deliberately not
  localized &mdash; see
  [walk-through &rarr; Never localized](../ui/walk-through.md#never-localized).
- **Sticky conversation headers painted over scrolling messages.** Intended.
- **Chat-list row titles ellipsised.** Chat names are user data, not catalog
  strings, and the row already identifies the chat by avatar.

## Suggested regression guard

Every failure above is detectable from the DOM without a screenshot, which
makes it cheap to keep fixed. Two options, in order of value:

1. **Extend `tests/ts/e2e/ui-localization-smoke.test.ts`.** It already visits
   each screen per language. Add a Max pass that asserts, for a fixed list of
   selectors, `scrollWidth <= clientWidth + 1` and `getBoundingClientRect()`
   inside the viewport. That turns B1, M3, M4, M6 and M7 into test failures.
2. **A CSS review rule.** `max-w-*` combined with `whitespace-nowrap`, and
   `overflow-hidden` without `text-overflow`, are the two shapes behind six of
   the nine categories. Both are greppable.

The scanner used for this audit is described in
[walk-through &rarr; Automating the walk](../ui/walk-through.md#automating-the-walk).
