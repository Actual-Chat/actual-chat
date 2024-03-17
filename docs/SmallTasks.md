Places:
- [Andrey] Fix "Share" & "Forward" views (there is no vertical padding in places bar)
- ~~[DF]Fix duplicate author ID bug~~
- [AY] Make Chat -> Place migration available for everyone
    - Implement a common API for object upgrade/migration

Audio and transcription:
- ~~[AK] Add in-memory buffering: sometimes the beginning of your phrase isn't transcribed due to VAD / disconnect~~
- [AK] Investigate why the transcript is sometimes wiped out / gets rewritten
- ~~[AK]Don't play "new message" sound if the event happened >= 30 seconds ago (it frequently plays when the app awakes)~~
- [AK] Configure listening turn-off period for chat

Onboarding:
- ~~No preselected default chats (all [x] to [ ])~~
- Allow name edits for each of default chats
- ~~[EK] Remove "Alumni" + change "Classmates" to "Classmates / Alumni"~~
- Add "[Your Last Name] Clan" (+ ask Grisha to come up with an icon)

Chat:
- ~~[AK] Your own author should be the first one in Members list~~
- Show bios in Members list
- Show anonymous chat members as a single "group" while their count is < 5 -- unless it's a peer chat
- Max. message length = 64K symbols?
- ~~[EK] "Copy" action for multiple messages should include contain author names.~~
- Add support for @u:userId mentions
  - Render them as authors if this author exists in chat
  - Otherwise render them as user mentions
  - Extend author info modal to show user info???

Permissions:
- [EK] Owners must be able to delete other people's messages

Performance:
- ~~[AK] Find out why collapsing items in Chat Members cause the panel to jitter while dragged~~ 

iOS/Android:
- Add "Disable file system cache" option in Settings/Application (+ explain it means it stores nearly nothing on the device)

Infrastructure / codebase:
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
- ~~SharedResourcePool must be IAsyncDisposable~~
- Remove Kubernetes project?
