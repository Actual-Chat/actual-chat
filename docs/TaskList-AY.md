Near-term:
- ISystemInfo + ServerClock
- Real-time playback: use ServerClock
- Real-time playback: don't render it as historical
- Counters, etc. - use InvalidationDelay  

Mid-term:
- AuthorModal - fix view (for you & anonymous authors)
- Anonymous user names: come up w/ nicer naming scheme
- Join anonymously: show a modal allowing to change your name + provide phone to send the link to re-join 

Backlog:
- Extract SessionService w/ proper sharding (+ use Redis?) and migrate to our own AuthService
- SettingsPanel / SettingsTab - make sure they inherit or use TabPanel / Tab
