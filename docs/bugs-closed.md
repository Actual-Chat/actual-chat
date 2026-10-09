# Closed bugs

Reports moved out of [bugs](./bugs.md) once they were fixed, rejected, or turned out not to be bugs. A verdict says why, and the evidence is the commit, pull request or issue that settles it. Commits are on `dev` unless stated.

| ID | Title | Reported by | Verdict | Evidence | Message |
|---|---|---|---|---|---|
| M9678 | Android: media resumes by itself when the user stops speaking | Alex | Fixed | 7caedbba42 (#4482); #4481 closed | [M9678](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9678) |
| M9684 | Modal forms: empty space under the Write button, anonymous block reserves room | Alex | Fixed | 79854c35da, 665ca3f67e, 5882f57391, c3ee27effd | [M9684](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9684) |
| M9694 | Toggle-safe-areas button broken, see-through strips with safe areas | Alex | Fixed | toggleSafeAreas works; 3f88117fbf (side strips not verified) | [M9694](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9694) |
| M9725 | Mobile: right panel opens on Threads | Andrey | Fixed | 4fb805caa2; #4852 closed | [M9725](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9725) |
| M9728 | Stale notifications not removed on Android and iPhone; tray counter stuck | Alex | Fixed | e35b0a8ade, b7c35726fe, a5684e63ff; #4546 closed | [M9728](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9728) |
| M9731 | Keyboard squeezes the header of a modal with a big header | Andrey | Fixed | c3ee27effd, 5882f57391 | [M9731](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9731) |
| M9748 | Sign-in phone/email field shows a wrong validation error | Dmitrii | Fixed | 523bf16e3b; #4725 closed | [M9748](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9748) |
| M9760 | Android app crashes when the network drops during a media fetch | Andrey | Fixed | 93065e935d | [M9760](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9760) |
| M9782 | Sign-in code step does not fit above the keyboard on iPhone | Frol | Fixed | 9b35e9fc7e; #5147 closed | [M9782](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9782) |
| M9794 | iPhone call: 'You' label clipped by the display corner | Frol | Fixed | 3e875f73ca; #5200 closed | [M9794](https://voxt.ai/chat/s-pmMsV1UVKG-fPHVtB5Zz0?n=9794) |
| B26211 | Windows app crashes when an image is pasted | Alexey | Fixed | 613530a7e3; #4459 closed | [B26211](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26211) |
| B26215 | Settings dropdown shows nothing selected | Dmitrii | Not a bug | Automatic is the default; the team said nothing was needed | [B26215](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26215) |
| B26220 | Desktop notifications stack in reverse order | Dmitrii | Fixed | ddeacdd255 | [B26220](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26220) |
| B26254 | Sign out/in: black screen, SharedResourcePool.Rent loops forever | Dmitrii | Fixed | 47f622899f; #4468 closed | [B26254](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26254) |
| B26294 | Onboarding stepper needs a Back button | Alex | Fixed | #4684 closed (weak evidence) | [B26294](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26294) |
| B26298 | Windows app hangs and cannot record (animation storm) | Alex | Fixed on dev | 33f13db496; not in release/v2.19, needs a cherry-pick | [B26298](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26298) |
| B26308 | No notifications for new messages | Alex | Fixed (per Alex) | Alex 2026-10-09; not verified in code | [B26308](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26308) |
| B26313 | Threads: no notifications, no unread counter, mention picker, back-to-chat | Alex | Fixed (per Alex) | Alex 2026-10-09; issues #4488–#4491 are still open, verify and close them | [B26313](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26313) |
| B26317 | Images and avatars sometimes don't load; WebView doesn't cache across restarts | Andrey | Fixed | 066004ac6d, 78236db28a; #4512 closed | [B26317](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26317) |
| B26347 | Sign-in SMS codes don't arrive (Tele2, Twilio ~50%) | Frol | Out of scope | Delivery tracking and routing shipped (1a4e8a7648, 08c173ee47, #5141); the WhatsApp resend is being added separately | [B26347](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26347) |
| B26390 | Translation stopped working (reasoning effort sent to models that reject it) | Alex | Fixed | 97b0f5e42a, 9f9e049ee6 | [B26390](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26390) |
| B26401 | Vertical line too close; wrong Virtualize build after .NET upgrade | Alexey | Fixed | 538bb90dae, f3c5f07fc8 | [B26401](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26401) |
| B26407 | Klipy GIF search and link previews broken (egress guard rejects IPv4-mapped addresses) | Alexey | Fixed | 041e992987, 11c2ad2119 | [B26407](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26407) |
| B26448 | iPhone sign-in form: phone field treated as text, cannot go back | Frol | Fixed | 516afd9917; #4509, #4510, #4725 closed (going back not verified) | [B26448](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26448) |
| B26473 | Video not cached on the device | Frol | Fixed | 066004ac6d, dbeca4f9ed; #4512 closed | [B26473](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26473) |
| B26478 | Chat created several times; owner could not delete or leave | Alex | Fixed | 635b2380cf; #4520, #4669 closed | [B26478](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26478) |
| B26507 | Translation not turned off for a stream already running | Frol | Fixed | d581b7e094; #4530 closed | [B26507](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26507) |
| B26511 | File download gives no feedback, then shows false 'Download failed' toasts | Andrey | Fixed | 935efe2faf; #4535, #5057 closed | [B26511](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26511) |
| B26520 | After app restart the Bugs chat opens instead of the chat list | Andrey | Fixed | ad113f40cc; #4536 closed | [B26520](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26520) |
| B26554 | Blazor Server circuit crashes after a call (two threads render one component) | Dmitrii | Fixed | 388b782ed4 (Fusion 14.4.16) | [B26554](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26554) |
| B26589 | Login modal moves down when the keyboard opens | Dmitrii | Fixed | c3ee27effd | [B26589](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26589) |
| B26603 | Onboarding shows the permissions step although all are granted | Dmitrii | Fixed | 9d18060e97, 205bfaeebc; #4833, #4583 closed | [B26603](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26603) |
| B26654 | Share-QR screen has wrong labels and a misplaced scan button | Alex | Fixed | 4e5253a327, 413f384d8f, 496201adf9 (moderate confidence) | [B26654](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26654) |
| B26733 | Telemetry consent buttons (Decline/Allow) | Frol | Fixed | 4e6819b0dc; #4607 closed | [B26733](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26733) |
| B26742 | Dev chat error: bad cast in ImageSuggestions.CanGenerateForPlace | Andrey | Fixed | 3e9e4f7af0, ae12d2e10b (Alexey, 2026-09-18) | [B26742](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26742) |
| B26748 | Dev down: migration HelmRelease stuck | Dmitrii | Fixed | PR #4633, edfa924de4 | [B26748](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26748) |
| B26760 | iPhone onboarding: microphone toggle can't be enabled when access is denied | Frol | Fixed | #4674 closed | [B26760](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26760) |
| B26794 | Sign-in: passkey button appears late and shifts the modal | Frol | Fixed | 0ec311bff9, f1f17c6dab | [B26794](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26794) |
| B26797 | Reload cat illustration is not cached | Andrey | Fixed | 96506270cc | [B26797](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26797) |
| B26798 | Something broken in web and apps after the AI picture work (CORS) | Alexey | Fixed | 1c1865dcb2; #4764 closed | [B26798](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26798) |
| B26809 | New avatar: name field comes up empty or wrong | Dmitrii | Fixed (probable) | a5c9c16aa8 | [B26809](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26809) |
| B26821 | Squashed layout on prod after an update | Frol | Not a bug | Old bundle; the update prompt is by design | [B26821](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26821) |
| B26857 | Proxy doesn't work on mobile networks (whitelists) | Frol | Fixed | 821cf62ad8; #4925 closed (the TestFlight build may have predated it) | [B26857](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26857) |
| B26867 | Coach tab safe areas; letter 'В' clipped; banner alignment | Dmitrii | Fixed (partly) | 4f56603acb, c94213862b; #5134 closed (clipped letter and banner blinking not verified) | [B26867](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26867) |
| B26931 | Screen sharing broken in Chrome and the Windows app | Alex | Fixed | 14d86f9dfb; #5033 closed | [B26931](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26931) |
| B26944 | Tabs too narrow in height; image frame rounded only at the bottom | Dmitrii | Fixed | 44da144b4d | [B26944](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26944) |
| B26968 | Border-only buttons and oversized pause bars in the live-session UI | Alex | Fixed (probable) | d779a2beca, 7fe9136f4d | [B26968](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26968) |
| B26972 | Live block hides older messages, no Expand button | Frol | Fixed | 12571de959, d779a2beca; #5079 closed | [B26972](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=26972) |
| B27044 | Link previews shown wrong; chat picture not applied | Alex | Fixed | 78236db28a (picture-not-applied not verified) | [B27044](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27044) |
| B27050 | Close X off-centre in the notification banner | Frol | Not a bug | Team decision: equidistant from top and right; #5127 still open for a smaller X | [B27050](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27050) |
| B27088 | Push-to-talk banner shown in the Windows app | Alex | Not a bug | Alexey: on desktop it can stay, PTT works while the app runs | [B27088](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27088) |
| B27092 | Text pasted from Apple Notes sends only the first line | Grigory | Fixed | ae2f09d429; #5175 closed | [B27092](https://voxt.ai/chat/s-pmMsV1UVKG-v3m8jr8kuj?n=27092) |
