# iOS / WebKit quirks

Each entry is a couple of sentences. The full write-up — commands, device ids, evidence — is in
`../references/ios/<name>.md`; open it when the one-liner turns out to matter.

**First rule: don't deploy to the iPhone to test a change.** Iterate with `/server-loop` and
`local.voxt.ai` in Chrome, which is ~8 s per rebundle against ~3 minutes plus a phone
interaction. Build and install on iOS only when Alex asks, or when the symptom is genuinely
WebKit-specific — and say which of the two it is rather than implying a browser pass covers iOS.

## Driving the device

- **The iPhone is tethered to `macmini`** (`ssh macmini`), where `ios_webkit_debug_proxy` bridges
  its inspector to a CDP-like protocol. → `../references/ios/ios-debugging-via-macmini.md`
- **iOS speaks the Target-wrapped WebKit protocol.** Plain `Runtime.evaluate` returns "'Runtime'
  domain was not found" — wrap every command in `Target.sendMessageToTarget` and read replies from
  `Target.dispatchMessageFromTarget`. `awaitPromise` is ignored, so arm in one call and read in a
  second. → `../references/ios/ios-app-driving-rig.md`
- **Live `AVAudioSession` state without a rebuild:** open the Audio Diagnostics modal and poll its
  label/value pairs; it re-invalidates every 3 s. `debugUI` is not exposed in dev-signed Release
  builds — click the DOM. → `../references/ios/ios-app-driving-rig.md`
- **Desktop Safari has no CDP** — it is driven with `safaridriver` + WebDriver BiDi; a ready
  harness lives at `~/bin/safari` on macmini, with four non-obvious gotchas.
  → `../references/ios/macos-safari-via-safaridriver.md`

## Channels that lie

- **Log to a file in the app container and pull it with `devicectl`.** `idevicesyslog` drops lines
  under load and the CDP bridge dies with the page, returning empty drains that look like "the
  event never happened". Build the lossless channel first.
  → `../references/ios/ios-device-diagnostics-channel.md`
- **Profile the device with `xctrace`, addressed by device *name*, after warming the tunnel** with
  a `devicectl` call. `--all-processes` is essential because WebCodecs work runs in the WebKit GPU
  process, not on the JS thread; thermal state comes free in the same trace.
  → `../references/ios/ios-device-cpu-profiling.md`
- **`element.scrollTop` is an integer on iOS** (fractional in Chrome), so sub-pixel corrections
  never land and accumulate a whole pixel at a time — the shape of several "moves 1px every time"
  bugs. → `../references/ios/ios-scrolltop-is-integer.md`

## Builds and signing

- **Prod Voxt and Voxt (Dev) both run from a folder named `ActualChat.app` on the phone.** Scope
  any pre-install SIGKILL to the dev app's container UUID, never to the folder name, and note the
  default `devicectl device info apps` listing hides the prod app.
  → `../references/ios/iphone-prod-and-dev-voxt-same-folder-name.md`
- **Dev provisioning profiles may lack the App Groups entitlement**, failing codesign with MT7140
  after a full compile; the workaround is to strip the entitlement, build, then restore it — never
  commit the stripped plists. → `../references/ios/macmini-ios-profiles-lack-app-groups.md`

The `/macmini` skill (user-level) covers the box itself, build and install flows.
