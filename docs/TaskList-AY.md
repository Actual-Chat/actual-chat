Fix:
- Unify TriggerEditor / focusAndOpenKeyboard
- LongPress - route to contextmenu + disable in Chrome?
- throttle - check its code for possible bugs / see how it is used in VirtualList
- serialize - remove limit
- updateVisibleKeysThrottled, updateViewportThrottled - remove limit, add timeout

Chat list:
- Remove tabs, leave "All" tab
- Add "Personal" tab
- Fix sorting menu - should sort order be per-tab?

Implement common variants of ErrorBoundary (timeout + reload, just show an error, etc.) + add it for:
- Pages
- Left panel
- Always visible items
- Modals
- Menus

Remove UpdateDelayer.Instant:
- ChatSettingsModal

Misc:
- CreationPanel / Page, + CreationModal - give proper names + review
- AuthorModal - get rid of CanAddContact, CanSendMessage + see how it's used
- UnreadCountSubHeader - make sure all unread counts don't pop on post every time
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab

Backlog:
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
