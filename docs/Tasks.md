Before release:

- Pre-rendered landing page [AY]
- Check all new text messages (esp. "Share" + troubleshooters) [AY]
- Phone sign-in: enable it [Frol]
- ~~Sign-in: replace sign-in menu w/ modal everywhere + remove menu [EK]~~
- ~~Languages: add Chinese, Korean, etc. + update settings page layout [DF]~~
- ~~New chat: 2-page layout [DF]~~
- Chat: empty chat must contain invite link + splash [DF]
- Audio activity: blinking "Listen" [Andrey]
- Onboarding: pre-create chats page
- Onboarding: use stored phone number [Frol]
- ~~Service worker / asset caching [EK]~~
- Stop historical playback in other chats when recording starts [Frol]
- Don't update SelectedChat when MiddlePanel isn't visible

- Critical bugs:
  - ~~iOS 16.1.1 - exit on startup [AK]~~
  - ~~Hot restart / WebView close: make sure the old view doesn't record [AK]~~
  - ~~Audio: use mono playback on Android? [AK]~~
  - "Verify phone" hangs the UI [Frol]
  - ~~Audio on iPhone: the latest prod version still triggers "no access to mic" sometimes [AK]~~
  - Investigate white screen issue [AY] 
- Important, but not critical bugs:
  - Chat: scrolling issues [AK]
  - Audio: use MediaStreamAudioSourceNode for EAC workaround instead of Audio element [AK]

ASAP:

- Check what's off w/ tracing / activities
- HEIC support

Near-term:

- Make sure server caching works as expected - we have had suspicious stats messages in the log with 0% hit
- Voice conversation is interrupted on pod scale-down

Mid-term (team):

- Extract Session service & migrate it to Redis
- ~~"Notes" chat [AK]~~
- "Search in this chat / everywhere" feature
- Efficient operation log monitoring and processing without re-reads
- Real-time playback somehow shows cached user activities sometimes
- Anonymous user names: come up w/ nicer naming scheme
- Custom Account IDs
- Import contacts & notify when some of your contacts register in Actual Chat

- Join as guest shouldn't be enabled by default in chats w/ anonymity enabled
- How private chat links work (no timer, no max. invite count, manually revoke, show the list of private links, but no "New private link" for public chats)
- Create chat should have ~ the same anonymity options as in Chat Settings
- "Join as guest": think of how key walk-through items should look like after this / onboarding
- "New message [in another chat]" notification banner
- Sound on any message, + different sound for voice messages w/ more intensive throttling
- Fix "Paste" action - there are almost always extra empty lines
- Think of how how & when to push a person who joined chat as guest to leave contact info. Ideally, show some dialog after his first message allowing him to sign in or leave this info.

Backlog (team):

- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
- Don't highlight SelectedChat(s) on mobile
