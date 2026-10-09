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
| GitHub issues (Actual-Chat/actual-chat) | issues created through #5201 (created 2026-10-09); linked issues re-read on the date at right | 2026-10-09 |
| Pull requests and commits on `origin/dev` | `39e862c03a` (2026-10-09 18:41 UTC) | 2026-10-09 |

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
| B27095 | High | Confirmed | Sessions expire while the user is active or signing in | Andrey | [#5220](https://github.com/Actual-Chat/actual-chat/issues/5220) issue, [#5225](https://github.com/Actual-Chat/actual-chat/issues/5225) (draft PR) | [B27095](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27095) |
| B26575 | High | Confirmed | Tapping a notification opens the Notifications section instead of the chat | Alexey | [#5216](https://github.com/Actual-Chat/actual-chat/issues/5216) issue, [#5221](https://github.com/Actual-Chat/actual-chat/issues/5221) (draft PR) | [B26575](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26575) |
| B26865 | High | Confirmed | Listening resumes after restart or after stopping, for about a minute | Alex | [#5204](https://github.com/Actual-Chat/actual-chat/issues/5204) open, [#5205](https://github.com/Actual-Chat/actual-chat/issues/5205) (draft PR) | [B26865](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26865) |
| B26303 | High | Confirmed | Call title keeps the wrong language after toggling translation | Andrey | — | [B26303](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26303) |
| B26936 | High* | Confirmed (investigate) | Live block freezes, no new transcripts or messages | Frol | [#4927](https://github.com/Actual-Chat/actual-chat/issues/4927) (related, open) | [B26936](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26936) |
| B26538 | High* | Confirmed (investigate) | Prod web page reloads itself every 1–5 minutes | Andrey | — | [B26538](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26538) |
| B26633 | Medium* | Confirmed | Chat view shows a blank screen instead of skeletons | Frol | — | [B26633](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26633) |
| B26587 | Medium* | Confirmed | Notification with the last message arrives after the call ended | Dmitrii | [#4594](https://github.com/Actual-Chat/actual-chat/issues/4594) (related, closed) | [B26587](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26587) |
| N1 | Medium* | Confirmed | Notifications panel: replace the '…' menu with a Clear button | Alex | [#5219](https://github.com/Actual-Chat/actual-chat/issues/5219) issue, [#5223](https://github.com/Actual-Chat/actual-chat/issues/5223) (draft PR) | — |
| B26811 | Medium* | Confirmed | HEIC image in the crop modal looks wrong | Andrey | — | [B26811](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26811) |
| B27005 | Medium* | Confirmed | Clicking a word starts the wrong playback ('trainer vs DJ') | Andrey | [#5211](https://github.com/Actual-Chat/actual-chat/issues/5211) issue, [#5212](https://github.com/Actual-Chat/actual-chat/issues/5212) (draft PR) | [B27005](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27005) |
| B27081 | Medium* | Confirmed | Video message shows as a broken file when transcoding fails | Alex | [#5162](https://github.com/Actual-Chat/actual-chat/issues/5162) open, [#5176](https://github.com/Actual-Chat/actual-chat/issues/5176) open | [B27081](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27081) |
| B26297 | Medium* | Confirmed | Share button is too wide, gaps are uneven | Alex | — | [B26297](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26297) |
| B26887 | Medium* | Confirmed | macOS link missing in the download-app modal | Alex | — | [B26887](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26887) |
| M9739 | Medium* | Confirmed (investigate) | Android ANR rate went up again | Alex | [#4957](https://github.com/Actual-Chat/actual-chat/issues/4957) open, [#4622](https://github.com/Actual-Chat/actual-chat/issues/4622) closed | [M9739](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9739) |
| B26487 | Medium* | Confirmed (investigate) | Right panel on iPhone shows no skeletons, only an empty screen | Frol | — | [B26487](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26487) |
| B27040 | Medium* | Needs check | Android microphone takes over a second to start recording | Alex | [#5140](https://github.com/Actual-Chat/actual-chat/issues/5140) open, [#5210](https://github.com/Actual-Chat/actual-chat/issues/5210) (draft PR, step 1) | [B27040](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27040) |
| B26964 | Medium* | Needs check | Klipy GIFs load very slowly | Andrey | [#5206](https://github.com/Actual-Chat/actual-chat/issues/5206) issue, [#5207](https://github.com/Actual-Chat/actual-chat/issues/5207) (draft PR) | [B26964](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26964) |
| B27078 | Medium* | Needs check | Voice and transcription settings open slowly after a refresh | Andrey | — | [B27078](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27078) |
| M9665 | Medium* | Needs check | iPhone: listening panel covers the Back arrow in the chat header | Alex | [#5104](https://github.com/Actual-Chat/actual-chat/issues/5104) open, [#5218](https://github.com/Actual-Chat/actual-chat/issues/5218) (draft PR) | [M9665](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9665) |
| M9674 | Medium* | Needs check | Live session with the keyboard open leaves a big empty gap | Alex | — | [M9674](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9674) |
| B26842 | Medium* | Needs review | Push-to-talk: unclear enabling UI, wrong Russian string, missing in browsers | Alex | [#5173](https://github.com/Actual-Chat/actual-chat/issues/5173) open | [B26842](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26842) |
| B26849 | Medium* | Needs review | Unread-reaction badge stays in the chat list after reading | Frol | — | [B26849](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26849) |
| B26404 | Medium* | Needs review | Intrusive notifications play no sound | Alexey | — | [B26404](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26404) |
| M9761 | Medium* | Needs review | Scrolling down blurs the text and the 'down' button stops working | Alex | — | [M9761](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9761) |
| B26584 | Medium* | Kept in the list | Video call: phone overheats and audio latency keeps growing on Android | Alex | [#5137](https://github.com/Actual-Chat/actual-chat/issues/5137) (open), [#4811](https://github.com/Actual-Chat/actual-chat/issues/4811) (related, open), [#4559](https://github.com/Actual-Chat/actual-chat/issues/4559) (closed) | [B26584](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26584) |
| B27100 | Low* | Confirmed | Show the 'new messages' marker before a collapsed conversation that has new messages | Dmitrii | — | [B27100](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27100) |
| M9807 | Low* | Confirmed | Bottom strip turns white while the left panel is open (Chrome) | Alex | — | [M9807](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9807) |
| B27028 | Low* | Needs check | 'Real-time listening will be disabled' dialog on word click | Andrey | [#5208](https://github.com/Actual-Chat/actual-chat/issues/5208) issue, [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (draft PR) | [B27028](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27028) |
| B26510 | Low* | Needs check | Historic playback cut off abruptly | Frol | [#4663](https://github.com/Actual-Chat/actual-chat/issues/4663) (possibly related, open) | [B26510](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26510) |
| B27108 | Low* | Needs check | Live session collapses into an odd intermediate state | Frol | [#4425](https://github.com/Actual-Chat/actual-chat/issues/4425) (related, open), [#5213](https://github.com/Actual-Chat/actual-chat/issues/5213) issue, [#5214](https://github.com/Actual-Chat/actual-chat/issues/5214) (draft PR), [#5215](https://github.com/Actual-Chat/actual-chat/issues/5215) (follow-up: first expand click) | [B27108](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27108) |
| B26344 | Low* | Needs check | Stray bracket after a link in a message | Frol | [#5154](https://github.com/Actual-Chat/actual-chat/issues/5154) (candidate, closed), [#5217](https://github.com/Actual-Chat/actual-chat/issues/5217) issue, [#5224](https://github.com/Actual-Chat/actual-chat/issues/5224) (draft PR) | [B26344](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26344) |
| B26247 | Low | Needs review | Stale notifications after logout and login | Alex | [#4546](https://github.com/Actual-Chat/actual-chat/issues/4546) (related, closed) | [B26247](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26247) |
| M9778 | Low | Needs review | Push notification arrives seconds after opening the chat | Frol | — | [M9778](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9778) |
| M9767 | Low* | Needs review | Image viewer glitch after a fast swipe down | Andrey | — | [M9767](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9767) |
| B26333 | Low* | Needs review | Phone confirmation shows an error and allows confirming at once | Andrey | [#4725](https://github.com/Actual-Chat/actual-chat/issues/4725) (related, closed) | [B26333](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26333) |
| B26227 | Low* | Needs review | Google login fails on dev from the prod app | Alexey | — | [B26227](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26227) |
| B26279 | Low* | Needs review | Menu items have different font weights (400 vs 500) | Alex | — | [B26279](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26279) |
| B26446 | Low* | Needs review | Screenshot: something 'doesn't fit' on a screen | Alex | — | [B26446](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26446) |
| B26488 | Low* | Needs review | Empty space after the text of the first messages in the chat view | Dmitrii | — | [B26488](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26488) |
| B26535 | Low* | Needs review | Odd right margin | Alexey | — | [B26535](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26535) |
| B26586 | Low* | Needs review | Chat view flickers | Dmitrii | — | [B26586](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26586) |
| B26840 | Low* | Needs review | Passkeys are shown separately and inside Account | Dmitrii | [#4574](https://github.com/Actual-Chat/actual-chat/issues/4574) open | [B26840](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26840) |
| B26853 | Low* | Needs review | Mentions look wrong, empty markup string | Andrey | — | [B26853](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26853) |
| B26918 | Low* | Needs review | Digest delivery time is not localized, and the digest didn't arrive | Alex | [#4785](https://github.com/Actual-Chat/actual-chat/issues/4785) (related, closed) | [B26918](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26918) |
| N2 | Low* | Needs review | Replay after confirming the dialog needs an extra action to resume in Chrome | Alex | [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (related, draft PR) | — |
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

### B27095 — Sessions expire while the user is active or signing in

- **Priority / review:** High / Confirmed
- **Reported by:** Andrey (also: Alex)
- **Messages:** [B27095](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27095)
- **GitHub:** [#5220](https://github.com/Actual-Chat/actual-chat/issues/5220) issue, [#5225](https://github.com/Actual-Chat/actual-chat/issues/5225) (draft PR)
- **What it is:** Andrey was logged out on prod in the middle of typing a message. Alex says the same happens right after logging in, i.e. a session expires even though the app is in use.
- **Notes:** Alex (2026-10-09): this must not happen; find why a session expires while it is used. Verified live, cookie part only (2026-10-09): the FusionAuth.SessionId expiry moves forward after each page load (+52 s between two loads). The presence-driven session extension is not tested live. PR #5225.

### B26575 — Tapping a notification opens the Notifications section instead of the chat

- **Priority / review:** High / Confirmed
- **Reported by:** Alexey (also: Dmitrii, Alex)
- **Messages:** [B26575](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26575), [B26577](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26577)
- **GitHub:** [#5216](https://github.com/Actual-Chat/actual-chat/issues/5216) issue, [#5221](https://github.com/Actual-Chat/actual-chat/issues/5221) (draft PR)
- **What it is:** Opening a notification lands in the Notifications section rather than in the chat the notification is about. Alex also hits it; the only way to get to the chat is to click the notification inside the Notifications section.
- **Notes:** Alex (2026-10-09): you must jump to the chat. Hypothesis: it happens when the Notifications section is already open, because the notification target isn't implemented as a URL prefix. Related: N1. Code check (2026-10-09): hypothesis confirmed in part. The Notifications section is not a URL: it is NavbarUI.SelectedGroupId == 'unread', persisted in local settings, and ChatUI.SelectNavbarGroup (~lines 881–882, since 21ab965fd9) deliberately keeps it when a chat opens ('Back returns to the unread panel'). On phones, a tap on a notification for the chat that is already open (bare /chat/X link, e.g. attention pings) is a same-URL navigation that History.NavigateTo skips, so the left panel stays on Notifications. Fix in AutoNavigationUI.NavigateTo for notification-like reasons: select the Chats group when Unread is selected, and hide the panels explicitly on a same-URL navigation. The cause for ordinary message pushes is not proven and may need a device trace. Verified live (2026-10-09) on a 390x844 viewport: with the Notifications panel open over the current chat, a simulated notification tap (a NOTIFICATION_CLICK message, as the service worker sends it) hides the panel and returns the navbar to Chats. A real push and a cold start were not tested. PR #5221.

### B26865 — Listening resumes after restart or after stopping, for about a minute

- **Priority / review:** High / Confirmed
- **Reported by:** Alex
- **Messages:** [B26865](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26865), [B26966](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26966)
- **GitHub:** [#5204](https://github.com/Actual-Chat/actual-chat/issues/5204) open, [#5205](https://github.com/Actual-Chat/actual-chat/issues/5205) (draft PR)
- **What it is:** Chats you were listening to are saved and restored on restart, and they keep playing real-time audio for about a minute, ignoring the settings. The same happens after a live conversation, when real-time listening is switched off.
- **Notes:** Alex (2026-10-09): we must not resume at all. Also find where the 1 minute comes from: the limit is 0 by default and can be set to 15–30 s in settings. Code check (2026-10-09): two causes, both traced. (1) After a restart, ActiveChatsUI.FixStoredActiveChats keeps a stored IsListening chat if its last activity was within Constants.Audio.ListeningDuration (60 s), and StartStopListeningPlayers starts the player. Fix: set IsListening=false there (armed PTT chats are re-added by RestoreKeepListeningChats). (2) The 60 s: StopListeningWhenIdle uses the 'keep listening' setting only if the user recorded in that chat; otherwise a fixed 60 s ListeningDuration applies (deliberate, 754074ecf2, so joining muted stays usable). A restored chat, or one restored after a replay or attachment (StartReplay snapshot and restore), is always a pure listener, so the setting is ignored. Alex (2026-10-09), decided: there is no 'pure listener' concept; remove Constants.Audio.ListeningDuration and apply the user's listening-linger setting to every session. Do not resume listening on reload, i.e. do not store the active chats at all. Tracked in #5204. Verified live (2026-10-09, combined build verify/pr-batch, two Chromes with the virtual mic): user 2 records, user 1 joins as a listener (the Join button disappears); after a page reload while the conversation is still live the Join button is back, so listening was not restored. PR #5205.

### B26303 — Call title keeps the wrong language after toggling translation

- **Priority / review:** High / Confirmed
- **Reported by:** Andrey (also: Frol)
- **Messages:** [B26303](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26303), [B26324](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26324), [B26327](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26327)
- **GitHub:** —
- **What it is:** After turning translation to English on and off, the live conversation title stays in the wrong language. Frol tried switching to both English and Russian and nothing helped.
- **Notes:** Alex (2026-10-09): must be fixed. Code check (2026-10-09): medium-low confidence. The live title is generated once for all viewers by ConversationSummarizer (language = most common detected language of the entries, else 'same', which leaves the choice to the model), and live titles can't be translated at all: TranslationsBackend reads the conversation from the database, where a live session isn't until it closes. So the translation toggle can't change it. How the title became English is unproven (language detection pending or wrong, or the English prompt). Fix: detect the language of the entries' text instead of falling back to 'same'; optionally make live titles translatable. Needs a repro with the entries' detected languages.

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
- **Notes:** Alex (2026-10-09): suspect a memory leak. Repro with a fake crash or memory allocation to see how the app behaves; if it reloads, look for the leak.

## Medium priority

### B26633 — Chat view shows a blank screen instead of skeletons

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Frol (also: Alexey)
- **Messages:** [B26633](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26633), [B26645](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26645)
- **GitHub:** —
- **What it is:** In chats that aren't cached, or on a weak connection, the chat view stays blank grey for seconds. Alexey sees skeletons on Windows and in the browser with throttled 3G, Frol sees none on Mac and web.
- **Notes:** Alex (2026-10-09): needs a fix, there must be skeletons; the code apparently never reaches them.

### B26587 — Notification with the last message arrives after the call ended

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Dmitrii
- **Messages:** [B26587](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26587)
- **GitHub:** [#4594](https://github.com/Actual-Chat/actual-chat/issues/4594) (related, closed)
- **What it is:** Right after finishing a call with Frol, Dmitrii got a web notification containing Frol's last message, which he had already heard. This looks like a notification that wasn't suppressed for a heard message.
- **Notes:** Alex (2026-10-09): let's try to fix this. Related: #4594 (heard PTT utterances, closed).

### N1 — Notifications panel: replace the '…' menu with a Clear button

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Alex
- **Messages:** — (review comment)
- **GitHub:** [#5219](https://github.com/Actual-Chat/actual-chat/issues/5219) issue, [#5223](https://github.com/Actual-Chat/actual-chat/issues/5223) (draft PR)
- **What it is:** The '…' menu in the Notifications section has one item, Clear, which is hard to find. Replace it with a visible Clear button that asks for confirmation in a modal.
- **Notes:** Alex (2026-10-09), while reviewing the notification-tap bug; no chat message. Verified live (2026-10-09): the Notifications header has a visible Clear button and no '...' menu; it opens 'Clear all notifications?' with Cancel and Clear; confirming clears the list. PR #5223.

### B26811 — HEIC image in the crop modal looks wrong

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Andrey (also: Alexey)
- **Messages:** [B26811](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26811), [B26812](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26812), [B26815](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26815)
- **GitHub:** —
- **What it is:** Some HEIC pictures show up incorrectly in the avatar crop modal (screenshot). Andrey is looking at it together with the crop modal and notes the sizes can be roughly calculated.
- **Notes:** Alex (2026-10-09): needs a fix.

### B27005 — Clicking a word starts the wrong playback ('trainer vs DJ')

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Andrey
- **Messages:** [B27005](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27005)
- **GitHub:** [#5211](https://github.com/Actual-Chat/actual-chat/issues/5211) issue, [#5212](https://github.com/Actual-Chat/actual-chat/issues/5212) (draft PR)
- **What it is:** Historic playback started by clicking a word overlaps with something else; Andrey described it as the trainer and the DJ fighting. The screenshot is the only description.
- **Notes:** Alex (2026-10-09): needs a fix; it comes from the speech coach markup. Code check (2026-10-09): partly traced (~55%). In PlayableTextMarkupView (razor and ts), words marked by the speech coach get data-menu (CoachMarkMenu, primary trigger) and onClick returns early for them, so clicking a marked word opens the hint menu and never starts replay; this is by design. Before ae830548fc (6 Oct) an unscoped closest('[data-menu]') matched the message's own menu and word clicks did nothing, which may be what Andrey saw on 5 Oct. Alex (2026-10-09): keep this logic (a coach-marked word opens the hint menu) and add a menu for such words that offers both options: the coach hint and play from here. To implement: add a 'Play from here' item to CoachMarkMenu that calls the same StartReplay path. Verified live (2026-10-09, coaching switched on for a test account, 40 s of speech through the virtual mic): 5 words were marked; clicking one opens the hint menu ('Avoid filler words ...') without starting replay, the menu now also has 'Play from here', which starts the replay ('Replaying' banner) and closes the menu. PR #5212.

### B27081 — Video message shows as a broken file when transcoding fails

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Alex (also: Dmitrii)
- **Messages:** [B27081](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27081), [B27085](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27085)
- **GitHub:** [#5162](https://github.com/Actual-Chat/actual-chat/issues/5162) open, [#5176](https://github.com/Actual-Chat/actual-chat/issues/5176) open
- **What it is:** A video had a thumbnail and a size but was shown as a plain file because Google Transcoder rejected its input as malformed and the type fell back to octet-stream. The first fix shipped (9bf091c0d0, da64dda9a2); two follow-ups remain.
- **Notes:** Alex (2026-10-09): Dmitry will fix it. Owner: Dmitrii.

### B26297 — Share button is too wide, gaps are uneven

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Alex
- **Messages:** [B26297](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26297)
- **GitHub:** —
- **What it is:** The Share button reaches almost to the edges, which makes the gaps around it differ in size. Alex suggested narrowing it so the gaps are at least the width of the small button.
- **Notes:** Alex (2026-10-09): needs a fix. Interpretation to confirm: it belongs to the download-app modal described in B26887.

### B26887 — macOS link missing in the download-app modal

- **Priority / review:** Medium* / Confirmed
- **Reported by:** Alex (also: Alexey, Andrey)
- **Messages:** [B26887](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26887), [B26890](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26890)
- **GitHub:** —
- **What it is:** The modal that opens on 'Download app' lists QR codes for the app URLs, and the macOS link is not there. The Mac app has not passed App Store review yet, so there is nothing to link to.
- **Notes:** Alex (2026-10-09): needs a fix plus the macOS app link. Related to B26297.

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
- **GitHub:** —
- **What it is:** While media links and other content load, the right panel stays empty for several seconds, so it is unclear whether anything is there. Alex believes the right panel has no skeletons at all.
- **Notes:** Alex (2026-10-09): look at what could cause it without an iPhone for now.

### B27040 — Android microphone takes over a second to start recording

- **Priority / review:** Medium* / Needs check
- **Reported by:** Alex (also: Dmitrii)
- **Messages:** [B27040](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27040)
- **GitHub:** [#5140](https://github.com/Actual-Chat/actual-chat/issues/5140) open, [#5210](https://github.com/Actual-Chat/actual-chat/issues/5210) (draft PR, step 1)
- **What it is:** Pressing the record button on Android can take around a second before recording starts, which is too slow.
- **Notes:** Alex (2026-10-09): check whether it is still unfixed and look at PRs; Dmitrii was removing synchronous calls on this path. Code check (2026-10-09): open, clear trace, no fix on dev. Release to mic takes 1.2–1.4 s in four sequential steps: a MutePeer round trip on every press (RecorderToggle.razor, ~230 ms), the begin tune awaited before StartRecording (~260–330 ms), audio focus and communication-device switch in AudioRecorder / AndroidAudioFocusHelper (~520–650 ms, polls up to 1 s), AudioRecord creation (~100 ms). Fix order: call MutePeer only when the own member is muted; request focus concurrently with the tune; try RequestCommunicationDevice without waiting (unproven). The same trace is in #5140. Not verified: needs an Android device. PR #5210.

### B26964 — Klipy GIFs load very slowly

- **Priority / review:** Medium* / Needs check
- **Reported by:** Andrey (also: Alexey, Alex)
- **Messages:** [B26964](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26964), [B27001](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27001), [B27094](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27094), [B27099](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27099)
- **GitHub:** [#5206](https://github.com/Actual-Chat/actual-chat/issues/5206) issue, [#5207](https://github.com/Actual-Chat/actual-chat/issues/5207) (draft PR)
- **What it is:** GIFs from the Klipy picker take a long time to load. Root cause: they go through imagor, which downloads the whole GIF before returning it.
- **Notes:** Alex (2026-10-09): we agreed not to use imagor in the Klipy GIF selector (except for GIFs used in the past). Check whether that is done. Code check (2026-10-09): open. GifPicker.razor GifButton builds the image src with UrlMapper.GifProxyUrl (imagor, '0/' path) for the trending/search grid and for the Recent row. Fix: use gif.PreviewUrl directly in the picker (the CSP already allows static.klipy.com) and keep imagor for GIFs inside sent messages (UrlMarkupView.razor). Open question: whether the Recent row keeps imagor. Trade-off: viewers' IPs reach Klipy's CDN while the picker is open. Verified live (2026-10-09): all 24 trending previews in the GIF picker load directly from static.klipy.com (200, image/gif, rendered); none go through imagor. PR #5207.

### B27078 — Voice and transcription settings open slowly after a refresh

- **Priority / review:** Medium* / Needs check
- **Reported by:** Andrey
- **Messages:** [B27078](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27078)
- **GitHub:** —
- **What it is:** After a page refresh or the first launch, the Voice and Transcription settings take a very long time to open.
- **Notes:** Alex (2026-10-09): needs a check. Code check (2026-10-09): no proven culprit. TranscriptionSettings.razor renders nothing until ComputeState finishes, and ComputeState awaits in sequence: language settings, user app settings, the ListSuggestedDubVoices RPC (calls the speech-synthesis provider on a cold cache; most plausible, ~55%), and for admins GetOwnVoiceStatus (a code comment says it lags a second or more). Confirm by timing each await after a hard refresh. Fix: give the state an initial value and move the voices lookups to a secondary state. 'Voice' has no separate settings page; if Andrey meant the per-chat VoiceSettingsModal, that needs another look.

### M9665 — iPhone: listening panel covers the Back arrow in the chat header

- **Priority / review:** Medium* / Needs check
- **Reported by:** Alex (also: Dmitrii, Andrey, Frol)
- **Messages:** [M9665](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9665), [M9771](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9771), [M9776](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9776)
- **GitHub:** [#5104](https://github.com/Actual-Chat/actual-chat/issues/5104) open, [#5218](https://github.com/Actual-Chat/actual-chat/issues/5218) (draft PR)
- **What it is:** The live-session activity panel (speaker heads) is drawn over the Back arrow in the chat header, and after recording stops while listening continues, the chat icon and arrow disappear. Tapping the left part of the header still goes back.
- **Notes:** Alex (2026-10-09): a UI issue, check whether it is fixed. Related: #5104 (Android, speaker avatar replaces the Back button). Code check (2026-10-09): not fixed; same cause as #5104. Intentional design from 571cc36589: when the header collapses, the Back arrow gets opacity 0 (main.css, .layout-header.collapsed:has(.header-activity-panel-wrapper) .btn-header-back) and the pointer-events:none activity panel with the author heads is drawn over it; taps still reach the hidden arrow. Nothing expands the header when recording stops (chat-activity-panel.ts). Fix: keep the arrow visible and pad the panel to its right when collapsed (also fixes #5104); optionally expand the header when recording stops. Checked at CSS level only (2026-10-09, 390x844): with the header collapsed and an activity panel present, the Back arrow has opacity 1, no stylesheet rule hides it, and the panel's left padding is 3rem. Real overlap on a phone is not checked. PR #5218.

### M9674 — Live session with the keyboard open leaves a big empty gap

- **Priority / review:** Medium* / Needs check
- **Reported by:** Alex
- **Messages:** [M9674](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9674), [B26265](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26265)
- **GitHub:** —
- **What it is:** With a live conversation active and the keyboard open, a large unused area is left under the live panel.
- **Notes:** Alex (2026-10-09): UI issue, check whether it is fixed. Code check (2026-10-09): no clear trace; needs a repro or the screenshot. The inline call panel is a fixed 12rem (call-screen.css) and folds into an island through a Blazor round trip when the keyboard opens, so a timing gap is possible. No double-counted keyboard height or safe-area inset was found. The call screen and live panel folding were rewritten on 7 Oct (eb49cebfab), so the old behaviour may be gone. Next step: repro with debugUI.showKeyboard().

### B26842 — Push-to-talk: unclear enabling UI, wrong Russian string, missing in browsers

- **Priority / review:** Medium* / Needs review
- **Reported by:** Alex (also: Alexey, Andrey)
- **Messages:** [B26842](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26842), [B26843](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26843), [B27012](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27012), [B27026](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27026)
- **GitHub:** [#5173](https://github.com/Actual-Chat/actual-chat/issues/5173) open
- **What it is:** Alex enabled PTT in a chat with his wife and it switched on for both sides at once; Alexey explains it is chat-wide and the others get a consent banner. The setting is also missing in browsers (by design), and the Russian text of the mute gesture reads 'тише'.
- **Notes:** Alex says the UI doesn't show where PTT is enabled versus where the permission is confirmed.

### B26849 — Unread-reaction badge stays in the chat list after reading

- **Priority / review:** Medium* / Needs review
- **Reported by:** Frol (also: Alex)
- **Messages:** [B26849](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26849)
- **GitHub:** —
- **What it is:** The chat list shows an unread-reaction badge although Frol had already read everything. Alex agreed this sometimes happens.

### B26404 — Intrusive notifications play no sound

- **Priority / review:** Medium* / Needs review
- **Reported by:** Alexey (also: Alex)
- **Messages:** [B26404](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26404), [B26416](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26416)
- **GitHub:** —
- **What it is:** The notification arrives and the regular notification is shown, but the intrusive (annoying) alert does not play. Alex asked for a sample to see how it works.

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
- **GitHub:** —
- **What it is:** The 'new messages' divider sits after a conversation the reader hasn't read. Dmitrii proposes putting the badge before a collapsed conversation with unread messages and inside it when it is expanded.
- **Notes:** Alex (2026-10-09): agrees with Dmitrii; ideally the marker is shown before collapsed conversations that have new messages. Alex confirmed again in the chat (B27117). Owner: Dmitrii (proposal).

### M9807 — Bottom strip turns white while the left panel is open (Chrome)

- **Priority / review:** Low* / Confirmed / owner Alex
- **Reported by:** Alex
- **Messages:** [M9807](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9807), [M9808](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9808), [M9809](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9809)
- **GitHub:** —
- **What it is:** On a phone in Chrome (screenshot) the strip at the bottom of the page is the normal colour with the left panel closed and turns white once the panel is slid out. The logic that sets the strip colour evidently picks the wrong colour.
- **Notes:** Alex (2026-10-09): will try to fix it himself.

### B27028 — 'Real-time listening will be disabled' dialog on word click

- **Priority / review:** Low* / Needs check
- **Reported by:** Andrey (also: Alexey, Dmitrii, Alex)
- **Messages:** [B27028](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27028), [B27039](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27039)
- **GitHub:** [#5208](https://github.com/Actual-Chat/actual-chat/issues/5208) issue, [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (draft PR)
- **What it is:** After a refresh, clicking a message shows a dialog that real-time listening will be turned off for replay, although listening was never enabled. Alex explains the dialog appears when real-time listening is off and is a surprise for people who just click on text.
- **Notes:** Alex (2026-10-09): probably a false positive; check whether PTT triggered it (no real-time listening indicator was visible). Code check (2026-10-09): the dialog tests the wrong thing. ChatAudioUI.Players.cs StartReplay shows the confirm whenever GetListeningChatIds() is non-empty, and that returns the stored IsListening flag (ActiveChatsUI), not player state. The flag can come from the post-restart restore (see B26865, 60 s window) or from PTT arming (armed PTT chats get no idle watcher). Fix: show the dialog only when IsAnyPlaying(listening chats) is true, and still snapshot and clear the flags. Fixing B26865 removes the restore case. Verified live (2026-10-09): with nothing listening, clicking a message to replay shows no dialog; while listening to a live recording the dialog appears, and 'Yes' starts the replay (Replaying banner, the word highlight moves). Not testable here: listening flagged but nothing audible. PR #5209.

### B26510 — Historic playback cut off abruptly

- **Priority / review:** Low* / Needs check
- **Reported by:** Frol
- **Messages:** [B26510](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26510)
- **GitHub:** [#4663](https://github.com/Actual-Chat/actual-chat/issues/4663) (possibly related, open)
- **What it is:** Historic playback stopped suddenly, not frozen but simply ended.
- **Notes:** Alex (2026-10-09): needs a quick look for a possible explanation. Possibly related: #4663. Code check (2026-10-09): no clear trace. The report predates the replay fixes of 19–20 Sep (#4617). Candidates: any server exception in ReplayStreamMuxer.OnRun completes the stream normally and the client doesn't reconnect, so replay ends silently; a blob read failure ends that track (SendFrameAndAdvance); client-side stops (device wake, recording started elsewhere, maintenance, PTT). If it recurs, capture the server log lines 'OnRun: Failed for chat' / 'Error processing entry' and the client 'Replay stream failed for chat'. Hardening: send a terminal error to the client.

### B27108 — Live session collapses into an odd intermediate state

- **Priority / review:** Low* / Needs check
- **Reported by:** Frol (also: Dmitrii)
- **Messages:** [B27108](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27108), [B27116](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27116), [B27118](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27118), [B27120](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27120)
- **GitHub:** [#4425](https://github.com/Actual-Chat/actual-chat/issues/4425) (related, open), [#5213](https://github.com/Actual-Chat/actual-chat/issues/5213) issue, [#5214](https://github.com/Actual-Chat/actual-chat/issues/5214) (draft PR), [#5215](https://github.com/Actual-Chat/actual-chat/issues/5215) (follow-up: first expand click)
- **What it is:** Frol collapsed a session right after the 'Thanks' message and the chat view re-rendered into a muddled state; after a couple of chat switches it collapsed properly. Frol clarified that the collapse button says 'collapsed' while only part of the messages are collapsed, and the conversation footer comes after the messages that stayed open.
- **Notes:** Alex (2026-10-09): needs a quick look. Related: #4425 (live conversation on collapse shows just the title). Code check (2026-10-09): bug, clear trace. In ChatUI.Tiles.cs (~lines 594–605) a collapsed open block hides its tail only while the live conversation exists; when the session closes, the materialized closed block has an empty HiddenTailRange, so the last 10 or more rows (LiveFoldMath.MinTailEntryCount) render inside the block under a 'collapsed' toggle, followed by the footer. Switching chats or toggling again drops the retained block, which is why it self-heals. Fix: extend the condition to a collapsed, non-dissolving closed block (hidden range from the fold end to the block's end). Test: LiveConversationDisplayTest, collapse while latched, finalize, assert no leaf rows past the fold end. #4425 is probably a different path. Not verified live: needs a long live session with a foldable block, and the virtual mic only makes short ones. Covered by the new LiveConversationDisplayTest cases. PR #5214.

### B26344 — Stray bracket after a link in a message

- **Priority / review:** Low* / Needs check
- **Reported by:** Frol (also: Andrey, Alexey)
- **Messages:** [B26344](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26344)
- **GitHub:** [#5154](https://github.com/Actual-Chat/actual-chat/issues/5154) (candidate, closed), [#5217](https://github.com/Actual-Chat/actual-chat/issues/5217) issue, [#5224](https://github.com/Actual-Chat/actual-chat/issues/5224) (draft PR)
- **What it is:** A closing bracket appears right after a link; Andrey recalls seeing it before with a doubled link. Alexey says such links need special user info.
- **Notes:** Alex (2026-10-09): check whether anything related changed. Candidate: #5154 (markup: links, dividers and stream reset, closed). Code check (2026-10-09): no clear trace (screenshot unavailable). Plausible cause (~40%): a bare URL with a path swallows a trailing ')' or ']' (MarkupParser.cs WwwUrl / IsUrlChar / UrlPathRe), so '(see https://x.com/a)' links the bracket; nothing trims trailing punctuation. #5154 did not touch this; it only added titled and enclosed links. No test covers a bare URL in parentheses. Fix: trim unbalanced trailing ) ] } and sentence punctuation from the URL and emit it as text. Frol's original message text is needed to confirm. Verified live (2026-10-09): '(see https://example.com/a/b)' links without the ')', trailing '.' and '!' are trimmed, the Wikipedia link 'Sampling_(signal_processing)' stays whole, and '?q=?' keeps its '?'. PR #5224.

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
- **GitHub:** [#4725](https://github.com/Actual-Chat/actual-chat/issues/4725) (related, closed)
- **What it is:** The screen shows a validation error and the confirm action at the same time, which cannot both be right. Alexey: ask for the validation to be respected.
- **Notes:** Related: #4725 (phone/email field validation, closed).

### B26227 — Google login fails on dev from the prod app

- **Priority / review:** Low* / Needs review
- **Reported by:** Alexey (also: Alex)
- **Messages:** [B26227](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26227), [B26274](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26274)
- **GitHub:** —
- **What it is:** Logging in with Google from the prod app against the dev server fails with 'request invalid'; email login works and the reverse direction works. Alex suspects the redirect list configured at Google.

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

### B26918 — Digest delivery time is not localized, and the digest didn't arrive

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex (also: Dmitrii, Alexey)
- **Messages:** [B26918](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26918), [B26921](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26921)
- **GitHub:** [#4785](https://github.com/Actual-Chat/actual-chat/issues/4785) (related, closed)
- **What it is:** The digest time should be shown as am/pm for English. Alex also isn't receiving the digest; the flow may have stalled for his user, and Alexey notes it needs unread messages and a long absence.
- **Notes:** Related: #4785 (digest sent every hour, closed).

### N2 — Replay after confirming the dialog needs an extra action to resume in Chrome

- **Priority / review:** Low* / Needs review
- **Reported by:** Alex
- **Messages:** — (review comment)
- **GitHub:** [#5209](https://github.com/Actual-Chat/actual-chat/issues/5209) (related, draft PR)
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
