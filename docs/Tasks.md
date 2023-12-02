Near term:

- Default chats:
  - No preselected default chats (all [x] to [ ])
  - Replace "Alumni" with "[You name it] Clan", 
  - Allow name edits for each of default chats
  - Ask Grisha to come up with icon for "Clan" 
- Chat permissions:
  - Only owners can post
  - Later:
    - Max. voice fragment duration: [0 (Voice is disabled), 10, 30, 1 min., 3 min., 5min., no limit] seconds
    - Pause between voice fragments: [same as above + 10 min., 30 min., 1 hour]
    - Pause between text messages: [same as above]
- Anonymous chats:
  - Hide anonymous members unless there are N of them 
    - N should be 1 for all existing chats
    - N should be 1 for any P2P anonymous chat by default  

- Bugs:
  - SharedResourcePool -> IAsyncDisposable
  - Back button behavior on Android 
  - Virtual list: AK, please list all known issues here.

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
- ~~Fix "Paste" action - there are almost always extra empty lines~~
- Think of how how & when to push a person who joined chat as guest to leave contact info. Ideally, show some dialog after his first message allowing him to sign in or leave this info.

Backlog (team):

- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
- Don't highlight SelectedChat(s) on mobile
