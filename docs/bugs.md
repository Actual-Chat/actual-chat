# Bugs

Active bug reports collected from the Voxt chats **Bugs** and **Bugs (Mobile)**, reconciled with GitHub issues and pull requests. Closed reports live in [bugs-closed](./bugs-closed.md). The `/review-bugs` skill keeps both files up to date.

## Scan state

| Chat | Chat id | Scanned messages | Time range (UTC) | Last scan |
|---|---|---|---|---|
| Bugs | `s-pmMsV1UVKG-v3m8jr8kuj` | #26211 – #27120 | 2026-09-09 10:40 – 2026-10-09 18:48 | 2026-10-09 |
| Bugs (Mobile) | `s-pmMsV1UVKG-fPHVtB5Zz0` | #9662 – #9809 | 2026-09-10 02:39 – 2026-10-09 18:53 | 2026-10-09 |

The first scan covered the month before 2026-10-09. Messages older than the first
id above were not read. The next scan starts from the message after the last id.
Second scan: 2026-10-09 18:54 UTC, 4 new messages in Bugs and 3 in Bugs (Mobile).

| Source | Reconciled up to | Reconciled on |
|---|---|---|
| GitHub issues (Actual-Chat/actual-chat) | issues created through #5253 (created 2026-10-10); linked issues re-read on the date at right | 2026-10-10 |
| Pull requests and commits on `origin/dev` | `7cc62f1582` (2026-10-10 10:30 UTC) | 2026-10-10 |

## How to read this file

- **ID** is the chat letter plus the id of the first message of the report (`B` = Bugs, `M` = Bugs (Mobile)); `N` ids are items Alex raised in a review without a chat message. Message links open the report on voxt.ai.
- **Priority** is `High`, `Medium` or `Low`. A trailing `*` means the priority was proposed and Alex hasn't confirmed it.
- **Review** is one of: `Needs review` (nobody has looked at it yet), `Proposed` (an auto-review verdict that a human hasn't confirmed), `Confirmed` (reviewed by Alex; ready to fix), `Confirmed (investigate)` (real, the cause is unknown), `Needs check` (check whether it still reproduces or is already fixed), `Postponed`, `Kept in the list`.
- **GitHub** lists the issue that tracks the report. `related` or `maybe` means the issue is similar but not the same bug.
- When a report is fixed, rejected or turns out not to be a bug, move it to [bugs-closed](./bugs-closed.md) with a verdict.

## Summary

| ID | Priority | Review | Title | Reported by | GitHub | Messages |
|---|---|---|---|---|---|---|
| B26775 | High | Confirmed | Echo cancellation stops working in the browser and the Windows app | Frol | — | [B26775](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26775) |
| B26575 | High | Confirmed | Tapping a notification opens the Notifications section instead of the chat | Alexey | [#5216](https://github.com/Actual-Chat/actual-chat/issues/5216) issue, [#5221](https://github.com/Actual-Chat/actual-chat/issues/5221) (open PR) | [B26575](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26575) |
| B26936 | High* | Confirmed (investigate) | Live block freezes, no new transcripts or messages | Frol | [#4927](https://github.com/Actual-Chat/actual-chat/issues/4927) (related, open) | [B26936](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26936) |
| B26538 | High* | Confirmed (investigate) | Prod web page reloads itself every 1–5 minutes | Andrey | — | [B26538](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26538) |
| B26587 | Medium* | Confirmed | Notification with the last message arrives after the call ended | Dmitrii | [#4594](https://github.com/Actual-Chat/actual-chat/issues/4594) (related, closed), [#5232](https://github.com/Actual-Chat/actual-chat/issues/5232) issue, [#5240](https://github.com/Actual-Chat/actual-chat/issues/5240) (draft PR) | [B26587](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26587) |
| B26811 | Medium* | Confirmed | HEIC image in the crop modal looks wrong | Andrey | — | [B26811](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26811) |
| B27081 | Medium* | Confirmed | Video message shows as a broken file when transcoding fails | Alex | [#5162](https://github.com/Actual-Chat/actual-chat/issues/5162) open, [#5176](https://github.com/Actual-Chat/actual-chat/issues/5176) open | [B27081](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27081) |
| M9739 | Medium* | Confirmed (investigate) | Android ANR rate went up again | Alex | [#4957](https://github.com/Actual-Chat/actual-chat/issues/4957) open, [#4622](https://github.com/Actual-Chat/actual-chat/issues/4622) closed | [M9739](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9739) |
| B26487 | Medium* | Confirmed (investigate) | Right panel on iPhone shows no skeletons, only an empty screen | Frol | [#5245](https://github.com/Actual-Chat/actual-chat/issues/5245) issue, [#5249](https://github.com/Actual-Chat/actual-chat/issues/5249) (draft PR) | [B26487](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26487) |
| M9665 | Medium* | Needs check | iPhone: listening panel covers the Back arrow in the chat header | Alex | [#5104](https://github.com/Actual-Chat/actual-chat/issues/5104) open, [#5218](https://github.com/Actual-Chat/actual-chat/issues/5218) (open PR) | [M9665](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9665) |
| M9674 | Medium* | Needs check | Live session with the keyboard open leaves a big empty gap | Alex | — | [M9674](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9674) |
| B26849 | Medium* | Needs review | Unread-reaction badge stays in the chat list after reading | Frol | [#5233](https://github.com/Actual-Chat/actual-chat/issues/5233) issue, [#5241](https://github.com/Actual-Chat/actual-chat/issues/5241) (draft PR) | [B26849](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26849) |
| B26404 | Medium* | Needs review | Intrusive notifications play no sound | Alexey | — | [B26404](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26404) |
| M9761 | Medium* | Needs review | Scrolling down blurs the text and the 'down' button stops working | Alex | — | [M9761](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9761) |
| B26584 | Medium* | Kept in the list | Video call: phone overheats and audio latency keeps growing on Android | Alex | [#5137](https://github.com/Actual-Chat/actual-chat/issues/5137) (open), [#4811](https://github.com/Actual-Chat/actual-chat/issues/4811) (related, open), [#4559](https://github.com/Actual-Chat/actual-chat/issues/4559) (closed) | [B26584](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26584) |
| B27100 | Low* | Confirmed | Show the 'new messages' marker before a collapsed conversation that has new messages | Dmitrii | [#5235](https://github.com/Actual-Chat/actual-chat/issues/5235) issue, [#5243](https://github.com/Actual-Chat/actual-chat/issues/5243) (draft PR) | [B27100](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27100) |
| M9807 | Low* | Confirmed | Bottom strip turns white while the left panel is open (Chrome) | Alex | — | [M9807](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9807) |
| B27108 | Low* | Needs check | Live session collapses into an odd intermediate state | Frol | [#4425](https://github.com/Actual-Chat/actual-chat/issues/4425) (related, open), [#5213](https://github.com/Actual-Chat/actual-chat/issues/5213) issue, [#5214](https://github.com/Actual-Chat/actual-chat/issues/5214) (open PR), [#5215](https://github.com/Actual-Chat/actual-chat/issues/5215) (follow-up: first expand click) | [B27108](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27108) |
| B26247 | Low | Needs review | Stale notifications after logout and login | Alex | [#4546](https://github.com/Actual-Chat/actual-chat/issues/4546) (related, closed) | [B26247](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26247) |
| M9778 | Low | Needs review | Push notification arrives seconds after opening the chat | Frol | — | [M9778](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9778) |
| M9767 | Low* | Needs review | Image viewer glitch after a fast swipe down | Andrey | — | [M9767](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9767) |
| B26333 | Low* | Needs review | Phone confirmation shows an error and allows confirming at once | Andrey | [#4725](https://github.com/Actual-Chat/actual-chat/issues/4725) (related, closed), [#5246](https://github.com/Actual-Chat/actual-chat/issues/5246) issue, [#5250](https://github.com/Actual-Chat/actual-chat/issues/5250) (draft PR) | [B26333](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26333) |
| B26227 | Low* | Needs review | Google login fails on dev from the prod app | Alexey | — | [B26227](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26227) |
| B26279 | Low* | Needs review | Menu items have different font weights (400 vs 500) | Alex | — | [B26279](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26279) |
| B26446 | Low* | Needs review | Screenshot: something 'doesn't fit' on a screen | Alex | — | [B26446](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26446) |
| B26488 | Low* | Needs review | Empty space after the text of the first messages in the chat view | Dmitrii | — | [B26488](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26488) |
| B26535 | Low* | Needs review | Odd right margin | Alexey | — | [B26535](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26535) |
| B26586 | Low* | Needs review | Chat view flickers | Dmitrii | — | [B26586](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26586) |
| B26840 | Low* | Needs review | Passkeys are shown separately and inside Account | Dmitrii | [#4574](https://github.com/Actual-Chat/actual-chat/issues/4574) open | [B26840](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26840) |
| B26853 | Low* | Needs review | Mentions look wrong, empty markup string | Andrey | — | [B26853](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26853) |
| N2 | Low* | Needs review | Replay after confirming the dialog needs an extra action to resume in Chrome | Alex | [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (related, merged) | — |
| M9662 | Low* | Needs review | Samsung: screen glitch that fixed itself after a drag | Alex | — | [M9662](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9662) |
| B26780 | Low | Postponed | Call screen: 'Outgoing call' header drawn over the first message | Frol | [#4791](https://github.com/Actual-Chat/actual-chat/issues/4791) open, [#5018](https://github.com/Actual-Chat/actual-chat/issues/5018) (maybe fixed, closed) | [B26780](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26780) |
| B27101 | Low | Postponed | Call screen has two hang-up icons, switch-camera icon on the wrong side | Alexey | [#5118](https://github.com/Actual-Chat/actual-chat/issues/5118) (loosely related, open) | [B27101](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27101) |
| B26304 | Low | Postponed | An ongoing call is hard to notice in the chat | Andrey | — | [B26304](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26304) |

## High priority

### B26775 — Echo cancellation stops working in the browser and the Windows app

- **Priority / review:** High / Confirmed
- **Reported by:** Frol (also: Alex)
- **Messages:** [B26775](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26775), [B26778](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26778)
- **GitHub:** —
- **What it is:** Frol's dev browser picked up the keyboard and the music playing in the room and even transcribed the song lyrics. Alex sees the same on Windows; it seems to affect every scenario that goes through WebRTC.
- **Notes:** Alex (2026-10-09): a live bug, we will work on it. Idea: look for a separate echo canceller on Windows.

### B26575 — Tapping a notification opens the Notifications section instead of the chat

- **Priority / review:** High / Confirmed
- **Reported by:** Alexey (also: Dmitrii, Alex)
- **Messages:** [B26575](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26575), [B26577](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26577)
- **GitHub:** [#5216](https://github.com/Actual-Chat/actual-chat/issues/5216) issue, [#5221](https://github.com/Actual-Chat/actual-chat/issues/5221) (open PR)
- **What it is:** Opening a notification lands in the Notifications section rather than in the chat the notification is about. Alex also hits it; the only way to get to the chat is to click the notification inside the Notifications section.
- **Notes:** Alex (2026-10-09): you must jump to the chat. Hypothesis: it happens when the Notifications section is already open, because the notification target isn't implemented as a URL prefix. Related: N1. Code check (2026-10-09): hypothesis confirmed in part. The Notifications section is not a URL: it is NavbarUI.SelectedGroupId == 'unread', persisted in local settings, and ChatUI.SelectNavbarGroup (~lines 881–882, since 21ab965fd9) deliberately keeps it when a chat opens ('Back returns to the unread panel'). On phones, a tap on a notification for the chat that is already open (bare /chat/X link, e.g. attention pings) is a same-URL navigation that History.NavigateTo skips, so the left panel stays on Notifications. Fix (2026-10-10, reworked with Alex twice): a notification about a chat is that chat's link with two more parameters, nid (the notification id without the user id) and nui (0 chats UI, 1 notifications UI, auto), so the URL always changes and the panels hide on a phone; tapping the notification that is already open hides them through the navbar item's current-URL path. /n/ is kept for notifications themselves and redirects to what the notification is about. Pushes carry nid, so a tap needs no lookup. Verified live on a 430 px viewport: a row tap hides the panels, Back returns to them, a simulated push tap for the already open chat navigates and hides them. A real push and a cold start from a push were not tested. PR #5221.

### B26936 — Live block freezes, no new transcripts or messages

- **Priority / review:** High* / Confirmed (investigate)
- **Reported by:** Frol
- **Messages:** [B26936](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26936)
- **GitHub:** [#4927](https://github.com/Actual-Chat/actual-chat/issues/4927) (related, open)
- **What it is:** Frol's chat view froze: the live block stopped showing new transcripts and messages. Cause unknown; it happens inside the live block and its transcripts.
- **Notes:** Alex (2026-10-09): assume we investigate this. Possibly related: #4927 (message list scrolls slowly after joining a session).

### B26538 — Prod web page reloads itself every 1–5 minutes

- **Priority / review:** High* / Confirmed (investigate)
- **Reported by:** Andrey (also: Alexey, Dmitrii, Alex)
- **Messages:** [B26538](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26538)
- **GitHub:** —
- **What it is:** The page reloads on its own every minute to five minutes, more often after a long video call. The reporter was most likely in the WASM client, which seems to crash sometimes.
- **Notes:** Alex (2026-10-09): suspect a memory leak. Repro with a fake crash or memory allocation to see how the app behaves; if it reloads, look for the leak. Code check (2026-10-09): no defect found. Every reload in the web app is a recovery from a fault (AppRecovery.startReloading on #blazor-error-ui, a rejected reconnect, no Blazor.reconnect in WASM, a session replacement from MonitorSessionValidity, the 5 s reload of the maintenance page); none runs on a timer or a version poll. There is no guard against repeated reloads. PR #5225 (session expiry) does not explain a reload every 1-5 minutes. Most likely a WASM unhandled exception or memory pressure after a long video call; the console logs 'startReloading: triggered by ...'. Needs a repro with the console open.

## Medium priority

### B26587 — Notification with the last message arrives after the call ended

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Dmitrii
- **Messages:** [B26587](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26587)
- **GitHub:** [#4594](https://github.com/Actual-Chat/actual-chat/issues/4594) (related, closed), [#5232](https://github.com/Actual-Chat/actual-chat/issues/5232) issue, [#5240](https://github.com/Actual-Chat/actual-chat/issues/5240) (draft PR)
- **What it is:** Right after finishing a call with Frol, Dmitrii got a web notification containing Frol's last message, which he had already heard. This looks like a notification that wasn't suppressed for a heard message.
- **Notes:** Alex (2026-10-09): let's try to fix this. Related: #4594 (heard PTT utterances, closed). Fix (2026-10-09, not verified): a late call transcript no longer notifies. The race is inferred from code; no test was run. PR #5240.

### B26811 — HEIC image in the crop modal looks wrong

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Andrey (also: Alexey)
- **Messages:** [B26811](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26811), [B26812](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26812), [B26815](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26815)
- **GitHub:** —
- **What it is:** Some HEIC pictures show up incorrectly in the avatar crop modal (screenshot). Andrey is looking at it together with the crop modal and notes the sizes can be roughly calculated.
- **Notes:** Alex (2026-10-09): needs a fix. Code check (2026-10-09): no fix made. PicCropModal loads the HEIC in a plain <img> (no HEIC conversion) and trusts naturalWidth/naturalHeight; the layout maths is consistent, and the project's HEIC pipeline (HeifDecoder, with EXIF orientation) is not used there. The cause cannot be told without the screenshot or the file. Routing HEIC through HeifDecoder before the crop would be a design change.

### B27081 — Video message shows as a broken file when transcoding fails

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Alex (also: Dmitrii)
- **Messages:** [B27081](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27081), [B27085](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27085)
- **GitHub:** [#5162](https://github.com/Actual-Chat/actual-chat/issues/5162) open, [#5176](https://github.com/Actual-Chat/actual-chat/issues/5176) open
- **What it is:** A video had a thumbnail and a size but was shown as a plain file because Google Transcoder rejected its input as malformed and the type fell back to octet-stream. The first fix shipped (9bf091c0d0, da64dda9a2); two follow-ups remain.
- **Notes:** Alex (2026-10-09): Dmitry will fix it. Owner: Dmitrii.

### M9739 — Android ANR rate went up again

- **Priority / review:** Medium* / Confirmed (investigate)
- **Reported by:** Alex
- **Messages:** [M9739](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9739)
- **GitHub:** [#4957](https://github.com/Actual-Chat/actual-chat/issues/4957) open, [#4622](https://github.com/Actual-Chat/actual-chat/issues/4622) closed
- **What it is:** The Android not-responding rate rose again after 2.21. The 2.21 cold-start ANRs on low-tier devices are tracked in #4957.
- **Notes:** Alex (2026-10-09): the reported ANRs are fixed, but pull the newest crashes and ANRs from Google Play via chrome2, check Sentry, and fix more if possible.

### B26487 — Right panel on iPhone shows no skeletons, only an empty screen

- **Priority / review:** Medium* / Confirmed (investigate)
- **Reported by:** Frol
- **Messages:** [B26487](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26487)
- **GitHub:** [#5245](https://github.com/Actual-Chat/actual-chat/issues/5245) issue, [#5249](https://github.com/Actual-Chat/actual-chat/issues/5249) (draft PR)
- **What it is:** While media links and other content load, the right panel stays empty for several seconds, so it is unclear whether anything is there. Alex believes the right panel has no skeletons at all.
- **Notes:** Alex (2026-10-09): look at what could cause it without an iPhone for now. Fix (2026-10-09, builds, not seen on screen): the right panel's initial state has a chat but no tabs, and the full skeleton is shown only when the chat is null and is hidden on narrow screens; the panel now renders the tab and list skeletons while tabs load. PR #5249.

### M9665 — iPhone: listening panel covers the Back arrow in the chat header

- **Priority / review:** Medium* / Needs check
- **Reported by:** Alex (also: Dmitrii, Andrey, Frol)
- **Messages:** [M9665](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9665), [M9771](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9771), [M9776](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9776)
- **GitHub:** [#5104](https://github.com/Actual-Chat/actual-chat/issues/5104) open, [#5218](https://github.com/Actual-Chat/actual-chat/issues/5218) (open PR)
- **What it is:** The live-session activity panel (speaker heads) is drawn over the Back arrow in the chat header, and after recording stops while listening continues, the chat icon and arrow disappear. Tapping the left part of the header still goes back.
- **Notes:** Alex (2026-10-09): a UI issue, check whether it is fixed. Related: #5104 (Android, speaker avatar replaces the Back button). Code check (2026-10-09): not fixed; same cause as #5104. Intentional design from 571cc36589: when the header collapses, the Back arrow gets opacity 0 (main.css, .layout-header.collapsed:has(.header-activity-panel-wrapper) .btn-header-back) and the pointer-events:none activity panel with the author heads is drawn over it; taps still reach the hidden arrow. Nothing expands the header when recording stops (chat-activity-panel.ts). Fix: keep the arrow visible and pad the panel to its right when collapsed (also fixes #5104); optionally expand the header when recording stops. Checked at CSS level only (2026-10-09, 390x844): with the header collapsed and an activity panel present, the Back arrow has opacity 1, no stylesheet rule hides it, and the panel's left padding is 3rem. Real overlap on a phone is not checked. PR #5218.

### M9674 — Live session with the keyboard open leaves a big empty gap

- **Priority / review:** Medium* / Needs check
- **Reported by:** Alex
- **Messages:** [M9674](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9674), [B26265](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26265)
- **GitHub:** —
- **What it is:** With a live conversation active and the keyboard open, a large unused area is left under the live panel.
- **Notes:** Alex (2026-10-09): UI issue, check whether it is fixed. Code check (2026-10-09): no clear trace; needs a repro or the screenshot. The inline call panel is a fixed 12rem (call-screen.css) and folds into an island through a Blazor round trip when the keyboard opens, so a timing gap is possible. No double-counted keyboard height or safe-area inset was found. The call screen and live panel folding were rewritten on 7 Oct (eb49cebfab), so the old behaviour may be gone. Next step: repro with debugUI.showKeyboard(). Code check (2026-10-09): no defect found. There is no double-counted keyboard inset; the call screen collapses to an island on the keyboard but only after a Blazor round trip, with a fixed 12rem inline height until then, and the map panel (a fixed 12rem) never folds on the keyboard. Needs a screenshot or an on-device repro.

### B26849 — Unread-reaction badge stays in the chat list after reading

- **Priority / review:** Medium* / Needs review
- **Reported by:** Frol (also: Alex)
- **Messages:** [B26849](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26849)
- **GitHub:** [#5233](https://github.com/Actual-Chat/actual-chat/issues/5233) issue, [#5241](https://github.com/Actual-Chat/actual-chat/issues/5241) (draft PR)
- **What it is:** The chat list shows an unread-reaction badge although Frol had already read everything. Alex agreed this sometimes happens.
- **Notes:** Fix (2026-10-09, not verified live): dismiss the reaction notification when its message is removed. Only one cause of a stale badge; old unseen reactions on existing messages still keep it (by design). PR #5241.

### B26404 — Intrusive notifications play no sound

- **Priority / review:** Medium* / Needs review
- **Reported by:** Alexey (also: Alex)
- **Messages:** [B26404](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26404), [B26416](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26416)
- **GitHub:** —
- **What it is:** The notification arrives and the regular notification is shown, but the intrusive (annoying) alert does not play. Alex asked for a sample to see how it works.
- **Notes:** Code check (2026-10-09): probably already fixed on dev. 'Intrusive' is NotificationKind.Attention; on Android the channel sound was a numeric resource URI that a rebuild could orphan (sound silent, vibration fine). 31b023bbac (2026-09-03) switched to a name-based URI and 61b9e22334 (2026-09-13) versioned the channel id. Confirm on a device with a current build; the web push cannot play a custom sound at all.

### M9761 — Scrolling down blurs the text and the 'down' button stops working

- **Priority / review:** Medium* / Needs review
- **Reported by:** Alex
- **Messages:** [M9761](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9761)
- **GitHub:** —
- **What it is:** Alex noticed two UI bugs on his own device: scrolling down makes the text look smeared, as if it were moved up and down every millisecond, and the 'down' button stops working. Others haven't seen it.

### B26584 — Video call: phone overheats and audio latency keeps growing on Android

- **Priority / review:** Medium* / Kept in the list
- **Reported by:** Alex (also: Alexey)
- **Messages:** [B26584](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26584), [B26594](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26594), [B26597](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26597)
- **GitHub:** [#5137](https://github.com/Actual-Chat/actual-chat/issues/5137) (open), [#4811](https://github.com/Actual-Chat/actual-chat/issues/4811) (related, open), [#4559](https://github.com/Actual-Chat/actual-chat/issues/4559) (closed)
- **What it is:** In a five-person video call the phone got hot and its audio latency grew until it hit the 60 s cap. Alexey found that mobile clients advertise AV1 (fixed in #4559) and that streams are re-registered every ~2 s.
- **Notes:** Alex (2026-10-09): keep it in the list for now.

## Low priority

### B27100 — Show the 'new messages' marker before a collapsed conversation that has new messages

- **Priority / review:** Low* / Confirmed
- **Reported by:** Dmitrii
- **Messages:** [B27100](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27100), [B27117](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27117)
- **GitHub:** [#5235](https://github.com/Actual-Chat/actual-chat/issues/5235) issue, [#5243](https://github.com/Actual-Chat/actual-chat/issues/5243) (draft PR)
- **What it is:** The 'new messages' divider sits after a conversation the reader hasn't read. Dmitrii proposes putting the badge before a collapsed conversation with unread messages and inside it when it is expanded.
- **Notes:** Alex (2026-10-09): agrees with Dmitrii; ideally the marker is shown before collapsed conversations that have new messages. Alex confirmed again in the chat (B27117). Owner: Dmitrii (proposal). Fix (2026-10-09, not verified live): the line is emitted before a collapsed unread card; unit tests cover only the unread predicate. PR #5243.

### M9807 — Bottom strip turns white while the left panel is open (Chrome)

- **Priority / review:** Low* / Confirmed / owner Alex
- **Reported by:** Alex
- **Messages:** [M9807](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9807), [M9808](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9808), [M9809](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9809)
- **GitHub:** —
- **What it is:** On a phone in Chrome (screenshot) the strip at the bottom of the page is the normal colour with the left panel closed and turns white once the panel is slid out. The logic that sets the strip colour evidently picks the wrong colour.
- **Notes:** Alex (2026-10-09): will try to fix it himself.

### B27108 — Live session collapses into an odd intermediate state

- **Priority / review:** Low* / Needs check
- **Reported by:** Frol (also: Dmitrii)
- **Messages:** [B27108](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27108), [B27116](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27116), [B27118](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27118), [B27120](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27120)
- **GitHub:** [#4425](https://github.com/Actual-Chat/actual-chat/issues/4425) (related, open), [#5213](https://github.com/Actual-Chat/actual-chat/issues/5213) issue, [#5214](https://github.com/Actual-Chat/actual-chat/issues/5214) (open PR), [#5215](https://github.com/Actual-Chat/actual-chat/issues/5215) (follow-up: first expand click)
- **What it is:** Frol collapsed a session right after the 'Thanks' message and the chat view re-rendered into a muddled state; after a couple of chat switches it collapsed properly. Frol clarified that the collapse button says 'collapsed' while only part of the messages are collapsed, and the conversation footer comes after the messages that stayed open.
- **Notes:** Alex (2026-10-09): needs a quick look. Related: #4425 (live conversation on collapse shows just the title). Code check (2026-10-09): bug, clear trace. In ChatUI.Tiles.cs (~lines 594–605) a collapsed open block hides its tail only while the live conversation exists; when the session closes, the materialized closed block has an empty HiddenTailRange, so the last 10 or more rows (LiveFoldMath.MinTailEntryCount) render inside the block under a 'collapsed' toggle, followed by the footer. Switching chats or toggling again drops the retained block, which is why it self-heals. Fix: extend the condition to a collapsed, non-dissolving closed block (hidden range from the fold end to the block's end). Test: LiveConversationDisplayTest, collapse while latched, finalize, assert no leaf rows past the fold end. #4425 is probably a different path. Not verified live: needs a long live session with a foldable block, and the virtual mic only makes short ones. Covered by the new LiveConversationDisplayTest cases. PR #5214.

### B26247 — Stale notifications after logout and login

- **Priority / review:** Low / Needs review
- **Reported by:** Alex
- **Messages:** [B26247](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26247), [B26252](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26252), [B26253](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26253)
- **GitHub:** [#4546](https://github.com/Actual-Chat/actual-chat/issues/4546) (related, closed)
- **What it is:** After a logout and login a pile of old notifications showed up on iPhone prod and then vanished, and once in three tries the console showed a 'Chat is not found' error from the thread list.
- **Notes:** Alex (2026-10-09): low priority for now. Related: #4546 (thread notifications that never clear, closed).

### M9778 — Push notification arrives seconds after opening the chat

- **Priority / review:** Low / Needs review
- **Reported by:** Frol (also: Alex)
- **Messages:** [M9778](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9778)
- **GitHub:** —
- **What it is:** Several times a morning Frol opened a chat and got a push for it a couple of seconds later. Alex didn't recognise the behaviour.
- **Notes:** Alex (2026-10-09): low priority for now.

### M9767 — Image viewer glitch after a fast swipe down

- **Priority / review:** Low* / Needs review
- **Reported by:** Andrey (also: Alex)
- **Messages:** [M9767](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9767), [M9770](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9770)
- **GitHub:** —
- **What it is:** Open an image, close it, then swipe down sharply, and the screen glitches (screenshot). Alex thinks it may not be iPhone-specific.

### B26333 — Phone confirmation shows an error and allows confirming at once

- **Priority / review:** Low* / Needs review
- **Reported by:** Andrey (also: Alexey)
- **Messages:** [B26333](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26333)
- **GitHub:** [#4725](https://github.com/Actual-Chat/actual-chat/issues/4725) (related, closed), [#5246](https://github.com/Actual-Chat/actual-chat/issues/5246) issue, [#5250](https://github.com/Actual-Chat/actual-chat/issues/5250) (draft PR)
- **What it is:** The screen shows a validation error and the confirm action at the same time, which cannot both be right. Alexey: ask for the validation to be respected.
- **Notes:** Related: #4725 (phone/email field validation, closed). Fix (2026-10-09, not run in the UI): the sign-in modal's Continue stayed enabled while the phone-or-email field showed an error; it is now also disabled when the form is invalid. The report names no screen, so this is an inference. PR #5250.

### B26227 — Google login fails on dev from the prod app

- **Priority / review:** Low* / Needs review
- **Reported by:** Alexey (also: Alex)
- **Messages:** [B26227](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26227), [B26274](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26274)
- **GitHub:** —
- **What it is:** Logging in with Google from the prod app against the dev server fails with 'request invalid'; email login works and the reverse direction works. Alex suspects the redirect list configured at Google.
- **Notes:** Code check (2026-10-09): no defect found. On a host override the prod app skips native Google sign-in and uses the web flow on the dev host with the dev server's own Google client; the voxt scheme is accepted since late July. Most likely a Google redirect_uri_mismatch: the dev OAuth client must list https://<override host>/signin-google. Check the error page (Google or the plain 'Invalid request.' text from MauiAuthController.Start).

### B26279 — Menu items have different font weights (400 vs 500)

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex
- **Messages:** [B26279](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26279)
- **GitHub:** —
- **What it is:** Alex set the weight to 400 on all menus because they differed, then realised they should probably all be 500. He chose not to change it right now.

### B26446 — Screenshot: something 'doesn't fit' on a screen

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex
- **Messages:** [B26446](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26446)
- **GitHub:** —
- **What it is:** Alex posted a screenshot with the comment 'doesn't fit'; the screen and the cause aren't stated.

### B26488 — Empty space after the text of the first messages in the chat view

- **Priority / review:** Low* / Needs review
- **Reported by:** Dmitrii
- **Messages:** [B26488](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26488), [B26489](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26489)
- **GitHub:** —
- **What it is:** When a chat view loads, the first two messages have empty space after the text; it corrects itself afterwards.
- **Notes:** Code check (2026-10-09): no defect found; the report has no screenshot or text, and the agent could not tell whether the gap is horizontal or vertical. The only element added after a message's text is the 'Unread' label on own messages. Needs a screenshot or a recording of the first frames.

### B26535 — Odd right margin

- **Priority / review:** Low* / Needs review
- **Reported by:** Alexey (also: Andrey)
- **Messages:** [B26535](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26535)
- **GitHub:** —
- **What it is:** A screenshot shows an unexplained right-hand margin; Alexey asked where it comes from and Andrey said he would look.

### B26586 — Chat view flickers

- **Priority / review:** Low* / Needs review
- **Reported by:** Dmitrii
- **Messages:** [B26586](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26586)
- **GitHub:** —
- **What it is:** The chat view blinks (screenshot); no further description.

### B26840 — Passkeys are shown separately and inside Account

- **Priority / review:** Low* / Needs review
- **Reported by:** Dmitrii (also: Alexey)
- **Messages:** [B26840](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26840), [B26841](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26841)
- **GitHub:** [#4574](https://github.com/Actual-Chat/actual-chat/issues/4574) open
- **What it is:** Dmitrii asked why passkeys appear both on their own and inside the account. Alexey: sign-in methods will be reworked into separate entries.

### B26853 — Mentions look wrong, empty markup string

- **Priority / review:** Low* / Needs review
- **Reported by:** Andrey
- **Messages:** [B26853](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26853), [B26856](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26856)
- **GitHub:** —
- **What it is:** Mentions render strangely in a Design chat message; the 'chat-message-markup not-streaming' element yields an empty string.
- **Notes:** Code check (2026-10-09): no defect found. About 30 mention inputs through MarkupParser and the formatters gave no empty or lossy output. The only suspicious spot is AuthorMentionView: a mention of a removed or not yet loaded author renders as an invalid badge showing '(n/a)'. The empty string in the report may come from how it was captured. Needs the original message (n=26853) and its HTML.

### N2 — Replay after confirming the dialog needs an extra action to resume in Chrome

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex
- **Messages:** — (review comment)
- **GitHub:** [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (related, merged)
- **What it is:** After confirming the replay dialog in Chrome, playback needs another action to resume. On mobile the dialog does not appear at all.
- **Notes:** Alex (2026-10-09), reported while testing #5209. Not reproduced in an automated Chrome run: after 'Yes' the Replaying banner appeared and the word highlight moved without an extra action, but scripted clicks count as user activation, so a real-browser autoplay-policy case is not excluded. Related: #5209.

### M9662 — Samsung: screen glitch that fixed itself after a drag

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex
- **Messages:** [M9662](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9662)
- **GitHub:** —
- **What it is:** Two screenshots of a glitch on a Samsung phone that corrected itself after dragging; Alex thinks it is probably Samsung's bug.

### B26780 — Call screen: 'Outgoing call' header drawn over the first message

- **Priority / review:** Low / Postponed
- **Reported by:** Frol (also: Dmitrii, Alexey)
- **Messages:** [B26780](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26780), [B26786](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26786), [B26789](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26789)
- **GitHub:** [#4791](https://github.com/Actual-Chat/actual-chat/issues/4791) open, [#5018](https://github.com/Actual-Chat/actual-chat/issues/5018) (maybe fixed, closed)
- **What it is:** After one call the layout shifts: the 'Outgoing call' label from the live session appears over the messages, and in MAUI an icon flies to the left.
- **Notes:** Alex (2026-10-09): postponed, calls aren't released yet. #5018 may already fix the root cause.

### B27101 — Call screen has two hang-up icons, switch-camera icon on the wrong side

- **Priority / review:** Low / Postponed
- **Reported by:** Alexey (also: Andrey)
- **Messages:** [B27101](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27101), [B27105](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27105), [B27106](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27106)
- **GitHub:** [#5118](https://github.com/Actual-Chat/actual-chat/issues/5118) (loosely related, open)
- **What it is:** The call screen shows two hang-up icons, and the switch-camera icon should be on the left.
- **Notes:** Alex (2026-10-09): postponed, calls aren't released yet. #5118 (hang-up decided in three places) is only loosely related.

### B26304 — An ongoing call is hard to notice in the chat

- **Priority / review:** Low / Postponed
- **Reported by:** Andrey (also: Alex)
- **Messages:** [B26304](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26304), [B26305](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26305), [B26306](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26306)
- **GitHub:** —
- **What it is:** The chat opens on the media tab, so it is almost invisible that a conversation is going on, and if you haven't joined it you can't find it at all.
- **Notes:** Alex (2026-10-09): postponed.
