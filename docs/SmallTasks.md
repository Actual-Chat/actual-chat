Mobile:
- [DF] Get rid of share intent state persistence on Android
- [Still intact?] Portrait/landscape mode switch should work in MAUI apps (mainly for images & videos) 

Recording, playback, transcription:
- [AK] Buffer up to 30s of audio
- 1.25x, 1.5x, 1.66x & 2x speedup for Historical playback
- Dynamic split pause detection:
    - Measure pauses (discarding the long ones, i.e. inter-phrase ones)
    - Compute average pause length
    - Split when pause exceeds the average one by 2-3x

Account settings:
- Replace "Full name" with "Real name", maybe even hide it. It's confusing w/ avatars
- Replace "Star" on default avatar with "Default", use "Make default" phrasing for other avatars

Chat:
- Show bios in Members list
- Chat & place background image (shown @ the top of Chat Settings tab)
- "New message [in another chat]" notification banner
- Max. message length = 64K symbols

Infrastructure / codebase:
- Remove Kubernetes project?
