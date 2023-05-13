Near-term:
- Fix online presence
- Fix "chat list order isn't stored / restored" + store it on server
- Switch to Stl.Rpc

Mid-term (team):
- Real-time playback: don't render it as historical
- AuthorModal - fix view (for you & anonymous authors)
- Anonymous user names: come up w/ nicer naming scheme
- Check if it's ok to run ComputeState not in Dispatcher - it is already like this on MAUI

- Join as guest shouldn't be enabled by default in chats w/ anonymity enabled
- How private chat links work (no timer, no max. invite count, manually revoke, show the list of private links, but no "New private link" for public chats)
- Create chat should have ~ the same anonymity options as in Chat Settings
- Animated gif of how recording works for recording button walk-through item
- "Install the app" banner
- "Join as guest": think of how key walk-through items should look like after this / onboarding
- "New message [in another chat]" notification banner
- Sound on any message, + different sound for voice messages w/ more intensive throttling
- Sign in with phone number
- Fix "Paste" action - there are almost always extra empty lines
- Think of how how & when to push a person who joined chat as guest to leave contact info. Ideally, show some dialog after his first message allowing him to sign in or leave this info.
- Fix "loading banner is never disappearing" in Firefox; ideally, add Firefox user agent detection.
- New feat: add "Notes" chat for everyone (found this quite useful - just created it manually)

Backlog (team):
- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
