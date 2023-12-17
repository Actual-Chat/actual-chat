Near term:

Urgent fixes:
- [!] No notif / unread on a message from someone who's not in your contact list
- [!] Android: sometimes theme change doesn't work / blue status bar color
- iOS: "Scroll down" sometimes doesn't disappear even when you're at the very bottom
- iOS: investigate weird "Back" click behavior (sometimes it does not work when you touch it, maybe related to Safari click event propagation or nearby clickalbe header)
- iOS: reconnect banner may take two lines on iPhone (not enough horizontal space)
- Mobile: Maybe we should pause AudioContext when nothing is playing, otherwise it drains the battery
- Anonymous chats: @ typing produces a list with missing avatars (+ we must use skeletons there)
- Anonymous chats: let's hide the list of participants until there are at least 5 of them (for the beginning) and add the option to control how many later
- Dark theme: fix Apple icon color on sign-in modal
- Portrait/landscape mode switch should work in MAUI apps. That's mainly for images & videos; maybe disable fullscreen video mode support on Android. Custom full screen implementation have issues with history if user exits from fullscreen mode with back button.
- [Done?] Theme should set text color + make sure all the issues w/ black on black in Dark theme are gone
- [Done?] Web splash should be the same as MAUI splash
- [Done?] Logout doesn't work on Android & Windows apps; maybe iOS as well.~~ (Working on Dev, Prod update is required)

Less urgent fixes:
- There are still some weird UI restarts on Android - prob. MauiLivenessProbe is too aggressive
- SharedResourcePool must be IAsyncDisposable

General:
- iOS: render correct unread message counter on app icon
  - See https://stackoverflow.com/questions/77007133/how-to-make-firebase-push-notification-increase-badge-count-when-receive-notific
- Add open graph tags for /chat/xxx & u/xxx URLs
- Web hook for posts
- Historical playback speedup
- New "Modal with tabs" - Andrey, you can start working on this somewhere in /test/
- Email digest (once per day):
  - The updates you've missed
  - Summary on your chat updates (list of chats & authors who posted there)
  - Summary on your activities (chats you wrote to, messages sent, the amount of time saved by talking, etc.)
- Custom chat & account IDs (actual.chat/u/xxx URLs, + similar ones for chats - should be aliases requiring no redirect)
- Application tab: move Server/WASM mode there
- Add "Auto" rendering mode (from .NET 8)
- Add "Disable file system cache" option (+ explain it means it stores nearly nothing on the device)
- Allow to set author's background image
- Allow to rename contacts + use your custom contact name for any author of a given user (unless anonymous)
- Pin chat/user to the left panel
- Join requests feature
- "New message [in another chat]" notification banner

Chat Settings panel:
- Allow to set chat background image (shown @ the top of Chat Settings tab)
- Show bios in Members list
- Show "last online @"

Default chats:
- No preselected default chats (all [x] to [ ])
- Replace "Alumni" with "[You name it] Clan", 
- Allow name edits for each of default chats
- Ask Grisha to come up with icon for "Clan"
 
Chat permissions:
- Only owners can post
- Owners must be able to delete other people's messages
- Allow/disallow reactions from others
- Require join to view the content above last N messages
- Later:
  - Max. voice fragment duration: [0 (Voice is disabled), 10, 30, 1 min., 3 min., 5min., no limit] seconds
  - Pause between voice fragments: [same as above + 10 min., 30 min., 1 hour]
  - Pause between text messages: [same as above]
  - Add Moderator role: like Owner, but can't assign roles

Anonymous chats:
- Hide anonymous members unless there are N of them 
  - N should be 1 for all existing chats
  - N should be 1 for any P2P anonymous chat by default  

Constraints:
- Max. message length = 64K symbols?

Less urgent bugs:
- Back button behavior on Android
- "Copy" for multiple messages should also contain author names.
- Swipe from the very right edge of the screen to remove the left panel doesn't work consistently

Mid-term (team):
- Refactor notifications
- Extract Session service & migrate it to Redis
- Join as guest shouldn't be enabled by default in chats w/ anonymity enabled
- How private chat links work (no timer, no max. invite count, manually revoke, show the list of private links, but no "New private link" for public chats)
- Create chat should have ~ the same anonymity options as in Chat Settings
- "Join as guest": think of how key walk-through items should look like after this / onboarding

Backlog (team):

- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
- Don't highlight SelectedChat(s) on mobile
