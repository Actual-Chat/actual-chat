Near-term:
- Fix left panel appearing-disappearing
- Check why there are 2 error badges for errors + make sure only unique errors appear

Mid-term:
- Real-time playback: don't render it as historical
- AuthorModal - fix view (for you & anonymous authors)
- Anonymous user names: come up w/ nicer naming scheme
- Check if it's ok to run ComputeState not in Dispatcher - it is already like this on MAUI

Backlog:
- ChatInfo & ChatState: get rid of one of these. ChatInfo = ChatState + ChatAudioState, i.e. doesn't change frequently enough to have a dedicated entity
- Join anonymously: show a modal allowing to change your name + provide phone to send the link to re-join
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
