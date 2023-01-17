Fix:
- Unify TriggerEditor / focusAndOpenKeyboard

Add helper calling UICommander.RunNothing + get rid of UpdateDelayer.Instant:
- RelatedChatEntry.Value + RelatedChatEntryPanel
- RightPanelUI.IsVisible.Value + ChatRightPanel

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
