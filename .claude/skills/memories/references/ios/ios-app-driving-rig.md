To debug the iOS app on Alex's tethered iPhone (`00008110-000405C426F1801E`, iPhone 13 Pro) without a
rebuild, drive its Blazor WebView over CDP from the macmini:

```bash
ssh macmini 'pkill -f ios_webkit_debug_proxy; nohup /opt/homebrew/bin/ios_webkit_debug_proxy \
  -c 00008110-000405C426F1801E:9222 -F > /tmp/iwdp.log 2>&1 &'
curl -s http://localhost:9222/json | grep -o "ws://[^\"]*"   # page id CHANGES on every app relaunch
```

The WebKit protocol needs **Target wrapping** — plain `Runtime.evaluate` returns
`'Runtime' domain was not found`. Listen for `Target.targetCreated`, then send
`Target.sendMessageToTarget {targetId, message: JSON.stringify({id, method, params})}` and read
replies from `Target.dispatchMessageFromTarget`. Node 22's built-in `WebSocket` is enough; no npm.
Working script lives at `/tmp/cdp.mjs` on the macmini.

`document.querySelector('.rec-btn').click()` toggles recording and works even under an open modal.
`debugUI` is **not** exposed in dev-signed Release builds — click the DOM instead.

**Reading live AVAudioSession state without a build:** open the Audio Diagnostics modal
(`.c-diagnostics-btn`, only rendered while there's call activity — turn recording on *first*), then
scrape `.diag-label`/`.diag-value` pairs. Its computed state auto-invalidates every 3 s, so polling
the DOM gives a ~3 s-resolution timeline of Category / Session mode / Output route / Scopes.

**Two traps that cost real time:**
- macOS has no `timeout` and no `setsid`; `log collect --device-udid` needs root. Detach background
  captures with `( nohup script < /dev/null > log 2>&1 & )`.
- Never write a monitor that *acts* on a missing reading. A poll loop that re-clicked the record
  button whenever the scrape came back empty toggled Alex's recorder 26 times when the modal closed,
  and the resulting "alternating" audio was mistaken for the bug under investigation.

See [[ios-device-diagnostics-channel]] for the lossless file-based log channel, and
[[ios-device-cpu-profiling]] for xctrace.
