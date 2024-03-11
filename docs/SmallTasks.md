Places:
- [Andrey] Fix "Share" & "Forward" views (there is no vertical padding in places bar)
- Fix duplicate author ID bug
- [AY] Make Chat -> Place migration available for everyone
    - Implement a common API for object upgrade/migration

Audio and transcription:
~~- [AK] Add in-memory buffering: sometimes the beginning of your phrase isn't transcribed due to VAD / disconnect~~
- Investigate why the transcript is sometimes wiped out / gets rewritten
- Don't play "new message" sound if the event happened >= 30 seconds ago (it frequently plays when the app awakes)

Onboarding:
- No preselected default chats (all [x] to [ ])
- Allow name edits for each of default chats
- Remove "Alumni" + change "Classmates" to "Classmates / Alumni"
- Add "[Your Last Name] Clan" (+ ask Grisha to come up with an icon)

Chat:
- Your own author should be the first one in Members list
- Show bios in Members list
- Show anonymous chat members as a single "group" while their count is < 5 -- unless it's a peer chat
- Max. message length = 64K symbols?
- "Copy" action for multiple messages should include contain author names.
- Add support for @u:userId mentions
  - Render them as authors if this author exists in chat
  - Otherwise render them as user mentions
  - Extend author info modal to show user info

Permissions:
- Owners must be able to delete other people's messages

Performance:
- Find out why collapsing items in Chat Members cause the panel to jitter while dragged 

iOS/Android:
- Add "Disable file system cache" option in Settings/Application (+ explain it means it stores nearly nothing on the device)

Infrastructure / codebase:
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
- SharedResourcePool must be IAsyncDisposable
- Remove Kubernetes project?
