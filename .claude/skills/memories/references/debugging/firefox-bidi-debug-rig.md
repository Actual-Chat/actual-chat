Firefox has no working CDP (`remote.active-protocols` defaults to BiDi-only, and
that pref needs a restart), so drive it over **WebDriver BiDi** on the
`--remote-debugging-port`. Alex's own Firefox runs with
`--remote-debugging-port=9333 --remote-allow-hosts=localhost,127.0.0.1`.

Two traps that cost real time:

1. **Firefox allows exactly one BiDi session and does NOT release it when the
   socket dies.** Killing the client process orphans the session and every later
   `session.new` fails with `session not created: Maximum number of active
   sessions` — with no way to recover but restarting Firefox. So the client must
   be a long-lived daemon that calls `session.end` on SIGINT/SIGTERM/exit, and
   eval must go through it (HTTP endpoint) rather than a new connection per call.
2. **Don't assume a port is free.** `9334` is adb on this machine; Firefox
   silently starts without a remote agent if the port is taken. `9421+` is free.
   Verify with `netstat -ano | grep <port>` AND check the owning PID is firefox.

For a scriptable instance that leaves Alex's browser alone, launch a second one
with its own profile and fake media:
`firefox.exe --remote-debugging-port=9421 --remote-allow-hosts=localhost,127.0.0.1
--profile <dir> --no-remote`, with `user.js` setting
`media.navigator.permission.disabled`, `media.navigator.streams.fake`,
`permissions.default.camera/microphone=1`, and
`security.enterprise_roots.enabled=true` (needed to trust local.voxt.ai).

`debugUI.signIn('test-*@actual.chat', {register:true})` works there. To take
video-call measurements without transcription cost, use **Join muted** in a chat
where someone else is already recording — see [[voxt-ui-perf-profile-client-first]].
Screenshots come from `browsingContext.captureScreenshot`; the remote-video
canvases are `transferControlToOffscreen`'d, so main-thread `getImageData` on
them throws and only a real screenshot shows what rendered.
