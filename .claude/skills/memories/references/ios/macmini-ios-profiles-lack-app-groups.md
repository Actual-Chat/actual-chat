Since `cf0f82c483` (2026-08-25, Frol, "render the share extension in the app's
theme" #4232) the iOS entitlements request
`com.apple.security.application-groups` = `group.chat.actual[.dev].app.shared`,
but **none** of the provisioning profiles on macmini grant it — not
`~/.ac-signing/*.mobileprovision` (newest 2026-08-05) nor any of the 14 in
`~/Library/MobileDevice/Provisioning Profiles/`. Every dev build therefore dies
at the signing step, after a full compile:

```
Platforms/iOS/Entitlements.dev.plist : error MT7140: The app requests the
entitlement 'com.apple.security.application-groups', but the provisioning
profile 'chat.actual.dev.app' doesn't contain this entitlement.
```

**Why:** the Apple portal App IDs `chat.actual.dev.app` and
`chat.actual.dev.app.share` need the **App Groups** capability enabled and the
group `group.chat.actual.dev.app.shared` added, then both dev profiles
regenerated and re-downloaded to the box. Portal-only; can't be done over ssh.
The same applies to the `.app.widget` App ID if the widget ever needs the group.

**How to apply:** if the profiles still lack it, strip the entitlement to
unblock — delete the `com.apple.security.application-groups` key+array from
`src/dotnet/App.Maui/Platforms/iOS/Entitlements.dev.plist` and
`src/dotnet/App.Maui.IosShareExt/Entitlements.dev.plist`, build, then
`git checkout --` them. The build then signs, installs and boots fine; the only
loss is that the share extension follows the system appearance instead of the
app's theme. Never commit the stripped plists. Note
`tmp/build-ios-dev.sh` starts with `git reset --hard origin/dev`, so strip
*after* the sync — or skip the sync and run only the `dotnet build` step. See
[[server-loop-owns-builds]] for the analogous "don't build by hand" rule on the
web side.
