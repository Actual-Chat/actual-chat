Add helper calling UICommander.RunNothing + get rid of UpdateDelayer.Instant:
- RelatedChatEntry.Value + RelatedChatEntryPanel
- RightPanelUI.IsVisible.Value + ChatRightPanel

Remove UpdateDelayer.Instant:
- ChatSettingsModal

Rename:
- _lastRenderedState -> _lastRenderedModel

Misc:
- CreationPanel / Page, + CreationModal - give proper names + review
- AuthorModal - get rid of CanAddContact, CanSendMessage + see how it's used
- UnreadCountSubHeader - make sure all unread counts don't pop on post every time
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab

