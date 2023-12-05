Near term:

Urgent fixes:
- Theme should set text color + make sure all the issues w/ black on black in Dark theme are gone
- Web splash should be the same as MAUI splash
- Portrait/landscape mode switch should work in MAUI apps (mainly for images & videos)
- There are still some weird UI restarts on Android - prob. MauiLivenessProbe is too aggressive
- Virtual list: AK, please list the remaining issues here
- [?] SharedResourcePool must be IAsyncDisposable

- General:
  - New "Modal with tabs" - Andrey, you can start working on this somewhere in /test/
  - Custom chat & account IDs (actual.chat/u/xxx URLs, + similar ones for chats - should be aliases requiring no redirect)
  - Add open graph tags for /chat/xxx & u/xxx URLs
  - Application tab: move Server/WASM mode there
  - Add "Auto" rendering mode (from .NET 8)
  - Add "Disable file system cache" option (+ explain it means it stores nearly nothing on the device)
  - Allow to set author's background image
  - Allow to rename contacts + use your custom contact name for any author of a given user (unless anonymous)
  - Pin chat/user to the left panel
- Chat Settings panel:
  - Allow to set chat background image (shown @ the top of Chat Settings tab)
  - Show bios in Members list
  - Show "last online @"
- Default chats:
  - No preselected default chats (all [x] to [ ])
  - Replace "Alumni" with "[You name it] Clan", 
  - Allow name edits for each of default chats
  - Ask Grisha to come up with icon for "Clan"
- Chat permissions:
  - Only owners can post
  - Owners must be able to delete other people's messages
  - Allow/disallow reactions from others
  - Later:
    - Max. voice fragment duration: [0 (Voice is disabled), 10, 30, 1 min., 3 min., 5min., no limit] seconds
    - Pause between voice fragments: [same as above + 10 min., 30 min., 1 hour]
    - Pause between text messages: [same as above]
    - Add Moderator role: like Owner, but can't assign roles
- Anonymous chats:
  - Hide anonymous members unless there are N of them 
    - N should be 1 for all existing chats
    - N should be 1 for any P2P anonymous chat by default  
- Constraints:
  - Max. message length = 64K symbols?

- Bugs:
  - Back button behavior on Android

Mid-term (team):
- Extract Session service & migrate it to Redis
- Efficient operation log monitoring and processing without re-reads
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
