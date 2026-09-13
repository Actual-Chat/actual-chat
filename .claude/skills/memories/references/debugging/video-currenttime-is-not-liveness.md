On a `<video srcObject=MediaStream>` (a camera track or a
MediaStreamTrackGenerator / VideoTrackGenerator), `currentTime` advances with the
**wall clock** for as long as the track is `live` — whether or not a single new
frame is being presented. `paused` stays `false` and `readyState` stays 4 too.
Measured on iOS 26 WebKit: a self-preview frozen on one image still reported
`dCt` of 14.992 s over 14.993 s of wall time, and 24 fps on the app's own
`.video-fps` meter.

`requestVideoFrameCallback` is no better on its own: it keeps firing (~15–24 Hz)
and `presentedFrames` keeps incrementing on a frozen element, while `mediaTime`
sits at exactly 0.

**The only reliable liveness signal is the pixels.** Draw the element into a
small offscreen canvas (32×32 is plenty), hash the `ImageData`, and compare
across samples spaced seconds apart. An all-black draw hashes to 0 with a
`h = h * 31 + byte` accumulator, so a non-zero constant hash means real, frozen
content rather than a failed `drawImage`.

This matters because `MstgPlaybackWatchdog`
(`src/dotnet/UI.Blazor.App/Components/VideoPanel/mstg-playback-watchdog.ts`)
detects stalls via `dCt < 50 ms` — so it is **blind to this failure mode** and
would report the frozen sender preview as healthy. Any watchdog for it has to
count frames upstream (writes into the preview writer) or hash pixels.

Cost me a wrong conclusion once already: I called a frozen preview "live" off
`currentTime` and had to retract it. See
[[ios-preview-mstg-freeze]] and [[headless-chrome-for-ui-measurement]].
