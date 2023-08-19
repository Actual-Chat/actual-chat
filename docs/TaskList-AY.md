Near-term:
- Fix manual reconnect
- Fix panel swipes
- Fix SessionToken refresh

Mid-term (team):
- Record how we use background audio playback for Apple review
- Extract Session service & migrate it to Redis
- "Notes" chat
- "Search in this chat / everywhere" feature
- "Install the app" banner
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
- Sign in with phone number
- Fix "Paste" action - there are almost always extra empty lines
- Think of how how & when to push a person who joined chat as guest to leave contact info. Ideally, show some dialog after his first message allowing him to sign in or leave this info.

Backlog (team):
- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
