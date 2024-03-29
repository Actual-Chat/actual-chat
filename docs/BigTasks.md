Near term:

Infrastructure:
- Add RpcMonitor

Potential fixes:
- [DF] Check share behavior + get rid of activity state persistence on Android
- [Andrey] iOS: reconnect banner may take two lines on iPhone (not enough horizontal space)
- [EK] Portrait/landscape mode switch should work in MAUI apps. That's mainly for images & videos; maybe disable fullscreen video mode support on Android. Custom full screen implementation have issues with history if user exits from fullscreen mode with back button.
- [AY] Fix auto-nav on mobile apps - it shouldn't bring you back to the same chat.
- iOS: investigate weird "Back" click behavior (sometimes it does not work when you touch it, maybe related to Safari click event propagation or nearby clickable header)

UX improvements:
- ~~[EK] Pin chat/user to the left panel~~
- Historical playback speedup
- Allow to rename contacts + use your custom contact name for any author of a given user (unless anonymous)
- "New message [in another chat]" notification banner
- Show bios in Members list
- Allow to set chat background image (shown @ the top of Chat Settings tab)

General:
- iOS: render correct unread message counter on app icon
  - See https://stackoverflow.com/questions/77007133/how-to-make-firebase-push-notification-increase-badge-count-when-receive-notific
- Add open graph tags for /chat/xxx & u/xxx URLs , ideally make them available for crawlers
- Web hook for posts
- New "Modal with tabs" - Andrey, you can start working on this somewhere in /test/
- Email digest (once per day):
  - The updates you've missed
  - Summary on your chat updates (list of chats & authors who posted there)
  - Summary on your activities (chats you wrote to, messages sent, the amount of time saved by talking, etc.)
- Custom chat & account IDs (actual.chat/u/xxx URLs, + similar ones for chats - should be aliases requiring no redirect)
- Add "Auto" rendering mode (from .NET 8)
- Allow to set author's background image
- Join requests feature

Permissions:
- Only owners can post
- Allow/disallow reactions from others
- Require join to view the content above last N messages
- Later:
  - Max. voice fragment duration: [0 (Voice is disabled), 10, 30, 1 min., 3 min., 5min., no limit] seconds
  - Pause between voice fragments: [same as above + 10 min., 30 min., 1 hour]
  - Pause between text messages: [same as above]
  - Add Moderator role: like Owner, but can't assign roles

Auth:
- Extract Sessions service w/ proper sharding (+ use Redis?) 
- Migrate to our own AuthService
- Get rid of User type

Less urgent bugs:
- Back button behavior on Android
- Swipe from the very right edge of the screen to remove the left panel doesn't work consistently

Mid-term (team):
- Refactor notifications
- Join as guest shouldn't be enabled by default in chats w/ anonymity enabled
- How private chat links work (no timer, no max. invite count, manually revoke, show the list of private links, but no "New private link" for public chats)
- Create chat should have ~ the same anonymity options as in Chat Settings
- "Join as guest": think of how key walk-through items should look like after this / onboarding

Backlog (team):
- ChatInfo & ChatState: get rid of one of these? ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
