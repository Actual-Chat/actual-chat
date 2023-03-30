Near-term:
- ChatView - check why it "stucks" sometimes & updates only after a few messages are posted

Mid-term:
- Real-time playback: don't render it as historical
- AuthorModal - fix view (for you & anonymous authors)
- Anonymous user names: come up w/ nicer naming scheme
- Join anonymously: show a modal allowing to change your name + provide phone to send the link to re-join 

Backlog:
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
