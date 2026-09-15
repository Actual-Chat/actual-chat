Debugging anything timing-sensitive on the iPhone: **write diagnostics to a file in the app
container and pull it off with `devicectl device copy from`.** The two obvious channels are both
lossy and will silently mislead you.

- `idevicesyslog -m <marker>` **drops lines under load** — captured 3 of ~10 events during a fast
  scroll. Counting from it produced two wrong root causes.
- The `ios_webkit_debug_proxy` + CDP bridge **dies with the page** (page id increments, in-page
  tracers vanish) and returns empty drains that look like "the event never happened".

Recipe: C# writes to `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData),
"navdiag.log")`, which lands at `Documents/navdiag.log` in the container. JS gets there through a
`[JSInvokable]` on a component the JS already holds a `blazorRef` to (added `DiagLog` to
`VirtualList`/`IVirtualListBackend`). Pull with:

```
xcrun devicectl device copy from --device <udid> --domain-type appDataContainer \
  --domain-identifier chat.actual.dev.app --source Documents/navdiag.log --destination /tmp/x.log
```

**Why:** a bug that only reproduces on device can't be reasoned out from a desktop proxy —
programmatic `scrollTop` never exercises the touch-only paths. Build the lossless channel *first*;
guessing ahead of it cost five wrong fixes and nine reproduction rounds of Alex's time.

Verifying a deploy actually landed: .NET string literals live in the UTF-16 `#US` heap (an ASCII
`grep` for them false-negatives), field names in the UTF-8 `#Strings` heap. For JS, grep the shipped
bundle for a *method/property* name — esbuild renames locals, so a local is a useless marker.

See [[macmini-ptt-profile-gap]] for the build/codesign side.
