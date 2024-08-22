In progress:
- [AndreyY] New real-time playback & recording panels
- [FC] Simple search & indexing
  - List near-term tasks & goals
- [AndrewK, AU, DF] AI search & indexing    
  - List near-term tasks & goals
- [EK] Email digest
  - List near-term tasks & goals
  
Candidate tasks:
- Rename any of your contacts
  - If you renamed someone, its name is used everywhere, though the image & bio are taken from avatar
  - Auto-rename the contact to your phone contact name when contact import finds a match
- New chat modes / settings
  - Only owners can post
    + Allow/disallow reactions others
    + [Later] Allow/disallow others to comment in threads
  - Max. voice fragment duration: [0 (Voice is disabled), 10, 30, 1 min., 3 min., 5min., no limit] seconds
  - Post cooldown: [same as above + 10 min., 30 min., 1 hour]
  - Public chats: require join to view more than N last messages
- Security
  - Auto-wipe:
    - For group chats, it's a chat-level option managed by owner
    - For private chats, it's an option applicable to messages of a given user
    - It should also be possible to activate it like this in private chats: 
      "Wipe all of my messages starting from here once they're read"
    - Grisha should come up with a way to display wipe timers (or maybe just fade out?)
  - Add "Disable file system cache" option in Settings/Application
    (+ explain it means the app stores nearly nothing on your device)
  - Think of E2E encryption/decryption.
- Add support for @u:userId mentions
  - Selector: use either your contacts or place contacts
  - @a:xxx should be used only for anonymous authors
  - [Later] Implement a migration to change all @a: to @u: except for anonymous authors 
  - Unify author info modal to support any PrincipalId
- Add support for :emoji: syntax
  - Selector ":" activates it in emoji picker mode (shows a line of emojis, tab & shift-tab moves the selection there)
  - Integrate https://github.com/missive/emoji-mart ?
  - AI emojis?
    - ":my-" allows to generate your own emoji with https://replicate.com/ & prompts from https://github.com/pondorasti/emojis
    - Admins will have an ability to make their own emojis available for everyone
- Add support for Tenor
  - ":" extends emoji picker with .gif picker?
- Offline action queue:
    - Enqueue + list queued actions for a given scope (chat)
    - Implement it for Post (w/ uploads)
    - Implement it for recorded audio
- API:
  - Generate your own API keys
  - Add support for use of API keys instead of Session
  - Web hook for posts
- Google / crawler support for any public chat & place: 
  - Open graph tags for /chat/xxx & u/xxx URLs
  - Render the most recent content (up to 1K messages?) - probably implement it as pre-rendering & fetching stored content
- Custom chat & account IDs (actual.chat/u/xxx URLs, should be aliases requiring no redirect)
- iOS: render correct unread message counter on app icon
    - See https://stackoverflow.com/questions/77007133/how-to-make-firebase-push-notification-increase-badge-count-when-receive-notific

Infrastructure / mostly non-UX candidate improvements:
- In-app notifications:
    - In-app notification list for any user (to show it on e.g. Windows app)
    - List active notifications - all or for a given scope (chat, place?)
    - Windows app should display Windows notifications relying on notification list API
    - Recompute notification state for a given notification
        - Auto-"store" on active -> dismissed change
        - Must be triggered by certain user actions (e.g. chat read position change)
    - Banners for certain notifications - e.g. "There are 3 new requests join this chat [Review]"
- Get rid of IAuth:
    - Extract Session management service from IAuth, shard it or use Redis
    - Get rid of IAuth & User, make IAccounts to do what IAuth does
- Use "Auto" rendering mode (from .NET 8)
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
