The iPhone is tethered to a Mac mini at host `macmini` (192.168.1.153), reachable
by `ssh macmini` with the default key. `ios_webkit_debug_proxy` runs there as
`-c null:9221,:9222-9230`: port 9221 lists devices, 9222 is the device's inspector,
speaking a CDP-like protocol, so the same `Runtime.evaluate` harnesses used for
Android over `adb` work against iOS.

**Why:** iOS/WebKit bugs on this project are otherwise undebuggable from Windows,
and reasoning about them from the code alone has produced several wrong diagnoses
in a row. Measurement is available — use it.

**How to apply:** before theorising about a WebKit-only symptom, check
`ssh macmini "idevice_id -l; pgrep -fl ios_webkit_debug_proxy"`, then attach to
`http://macmini:9222/json/list` exactly as with Android on 9444. See
[[headless-chrome-for-ui-measurement]] for the browser-measurement caveat.

**Two things the "same harness" claim glosses over**, both learned the hard way:
iOS 26 speaks the Target-wrapped WebKit protocol, so every command must be wrapped
in `Target.sendMessageToTarget` against the `page-*` target and replies arrive inside
`Target.dispatchMessageFromTarget`; and its `Runtime.evaluate` ignores `awaitPromise`,
returning the promise itself as `{}`. The socket also drops after a few seconds of
polling. So the working shape is: **arm in one invocation** (the probe stashes its
result on `globalThis.__r`), **read in a second one** a few seconds later. A ready
harness lives at `/tmp/ios-ev.mjs` on the box, with `/tmp/read.js` as the reader.

`ios_webkit_debug_proxy` is *not* always running - start it per session, and note
`idevice_id -l` going empty means the USB link dropped, which no amount of restarting
the proxy fixes. See [[ios-scrolltop-is-integer]] for what this was used to establish.
