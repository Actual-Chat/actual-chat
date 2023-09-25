Before release:
 
- ~~Pre-rendered landing page [AY]~~
- Add proper site image and description to _Host.cshtml [Andrey, Grisha]
- ~~Show <Recording> sign for streaming entries in chat list~~
- ~~Use max(activity date, contact creation date) in all sort modes to make sure new contacts go on top~~
- Check all new text messages (esp. ~~"Share"~~ + troubleshooters) [AY]
- ~~Phone sign-in: enable it [Frol]~~
- ~~Add more languages: Hindi, Bengali, Tamil, Modern Standard Arabic, Turkish, Vietnamese, Italian, Thai, Portuguese, Polish~~
- ~~Sign-in: replace sign-in menu w/ modal everywhere + remove menu [EK]~~
- ~~Languages: add Chinese, Korean, etc. + update settings page layout [DF]~~
- ~~New chat: 2-page layout [DF]~~
- ~~Chat: empty chat must contain invite link + splash [DF]~~
- Audio activity: blinking "Listen" [Andrey]
- ~~Onboarding: use stored phone number [Frol]~~
- ~~Service worker / asset caching [EK]~~
- ~~Push-to-talk must not change the playback state [AY]~~
- Stop historical playback in other chats when recording starts [Frol]
- ~~Don't update SelectedChat when MiddlePanel isn't visible~~
- ~~Fade out loading overlay [AY]~~
- ~~New Active Chats UX [AY]~~
- ~~Fix copy action text [AY]~~ 

- Critical bugs:
    - Share into the app: sometimes auto-navigation instantly closes share modal [DF]
    - 2 "Notes" chats on dev / no upgrade on prod [AK]
    - Android: echo problem is still there, but only sometimes on S23
    - Android: language switch triggers "No mic access" modal [AK]
    - "No mic access" -> "OK" shouldn't be there / should have "X" instead
    - ~~iOS: background playback issues [AK]~~
    - ~~iOS 16.1.1 - exit on startup [AK]~~
    - ~~Hot restart / WebView close: make sure the old view doesn't record [AK]~~
    - ~~Audio: use mono playback on Android? [AK]~~
    - ~~"Verify phone" hangs the UI [Frol] [Unable to reproduce]~~
    - ~~Audio on iPhone: the latest prod version still triggers "no access to mic" sometimes [AK]~~
    - Investigate white screen issue [AY]
- Important, but not critical bugs:
    - Chat: scrolling issues [AK]
    - Audio: use MediaStreamAudioSourceNode for EAC workaround instead of Audio element [AK]

Next week:

- Onboarding: pre-create chats page
- Onboarding: request mic access permission
- Onboarding: request notification permission
- Onboarding: request contacts permission
- Check what's off w/ tracing / activities
- HEIC support?

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
