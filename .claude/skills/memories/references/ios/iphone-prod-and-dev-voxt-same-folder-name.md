Prod Voxt (TestFlight, `chat.actual.app`) and Voxt (Dev) (`chat.actual.dev.app`) are both
installed on the phone and both bundles are usually named `ActualChat.app`, so
`devicectl device info processes` shows two `…/ActualChat.app/ActualChat` processes from
different container UUIDs. The default `devicectl device info apps` listing hides the prod
app; only `--include-all-apps` reveals it.

The dev folder name is not stable — on 2026-09-10 the installed dev bundle was `SX2.app`
before the build and `ActualChat.app` after — so match on the container UUID, never on the
folder name, in either direction.

**Why:** During `/macmini build` on 2026-09-02 a regex on the process path killed the prod
Voxt app twice, mistaking it for the "orphaned old bundle" case the macmini skill warns about.

**How to apply:** Before killing, read the dev app's container URL — the text output of
`devicectl device info apps` has no URL column, so use
`--bundle-id chat.actual.dev.app --include-all-apps --json-output <file>` and read
`result.apps[].url`. SIGKILL only PIDs whose path starts with that UUID (the app plus its
`PlugIns/*.appex`). Leave the other container alone.
See [[macmini]] skill, *Installing on the iPhone*.
