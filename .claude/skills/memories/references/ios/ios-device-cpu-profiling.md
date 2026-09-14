`xctrace` on macmini profiles the **physical iPhone** headlessly, system-wide —
the only way to see WebKit's out-of-process work, since WebCodecs encode/decode
runs in `com.apple.WebKit.GPU` / `WebContent`, not on the JS thread. A JS
profiler shows an idle pipeline even when a codec is burning the CPU.

```bash
export DEVELOPER_DIR=/Applications/Xcode-26.6.0.app/Contents/Developer
xcrun devicectl device info details --device 00008110-000405C426F1801E >/dev/null  # WARM THE TUNNEL FIRST
xcrun xctrace record --device "Alexander’s iPhone" --template "Time Profiler" \
  --all-processes --time-limit 3m --no-prompt --output /tmp/cap.trace
xcrun xctrace export --input /tmp/cap.trace \
  --xpath '/trace-toc/run[@number="1"]/data/table[@schema="time-profile"]' > tp.xml
```

Three traps, each of which cost time:

- **Address the device by NAME, not UDID.** `--device <UDID>` fails with
  "Timed out waiting for device to boot" even though `devicectl` sees it.
- **Warm the tunnel** with a `devicectl` call immediately before recording, in
  the same shell. Without it `xctrace` intermittently fails the same way.
- **`--all-processes` works on a device** and captures Apple daemons
  (`audiomxd`, `backboardd`, `videocodecd`, `cameracaptured`) — essential,
  because the app's own process is often a minority of the load.

`Time Profiler` also carries `device-thermal-state-intervals` (Nominal / Fair /
Serious / Critical) — export that tiny table first for an instant read on
whether a capture is throttled. `Activity Monitor`'s `sysmon-process` table
parses to kernel-only rows and is not worth using; Time Profiler's sample
weights give per-process CPU anyway.

Scale: 15 s ≈ 11 MB of exported XML, 3 min ≈ 188 MB. Budget ~5 min for
save+export+parse of a 3-min capture. Profiling overhead is ~5%
(`diagnosticd` + `DTServiceHub`) — subtract it.

Symbols resolve to **binary names** reliably (enough to tell `VideoToolbox`
from `libvpx`), but WebCore/QuartzCore **function** names come back as raw hex —
the device symbol set doesn't cover them; you'd need `atos` against the
DeviceSupport tree. Parser: `/tmp/analyze.py` (scratchpad `analyze.py`),
`tp` subcommand.

For live in-page questions use `ios_webkit_debug_proxy` + the Target-wrapped
protocol ([[ios-device-diagnostics-channel]]). Two gotchas: `Runtime.evaluate`
does **not** honour `awaitPromise` (an async IIFE returns `{}`), so do timing
in Node across two evaluates; and `getVideoPlaybackQuality()` returns all
zeros for MediaStream-backed `<video>` — use `requestVideoFrameCallback` to
measure presented fps.
