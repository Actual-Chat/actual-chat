**Never run `dotnet build` yourself while the loop is up.** Step 2 of every loop
iteration is dotnet-build, so a separate `dotnet build ActualChat.CI.slnf` builds
the whole solution (tests included) and then the loop builds it again - double
the wait for the same answer. Trigger the iteration and read the errors from
`tmp/server-loop-dotnet-build.log`; `npm run build:Verify` is the same story
against step 1, except when you specifically want eslint, which the loop skips.

Three things that cost real time when iterating with `server-loop` + the chrome MCP:

**Waiting for a rebuild.** `tmp/server-loop.log` is wiped at the *start* of each
iteration, but not instantly — for several seconds after `curl /health/stop` it
still holds the *previous* run's `Watchdog: started` line. Grepping for that
straight away matches the old run and reports success against a stale server.
Capture `head -1 tmp/server-loop.log` first and wait for it to change before
grepping for the terminal line:

```bash
OLD=$(head -1 tmp/server-loop.log); curl -sk https://local.voxt.ai/health/stop
for i in $(seq 1 90); do NEW=$(head -1 tmp/server-loop.log 2>/dev/null)
  if [ "$NEW" != "$OLD" ] && grep -qE "Watchdog: started|Last step failed" tmp/server-loop.log; then break; fi
  sleep 5; done
```

**A rebundle is invisible to an already-loaded page.** `touch
tmp/server-loop-rebundle` rebuilds the bundle without restarting the server, but
the `/dist/bundle.<hash>.*` URL is unchanged and served `immutable`, and the
service worker caches it too. `navigate_page` with `ignoreCache: true` is *not*
enough — the page kept serving pre-rebundle CSS. What works, from the page:

```js
(await caches.keys()).forEach(k => caches.delete(k));
(await navigator.serviceWorker.getRegistrations()).forEach(r => r.unregister());
```

then reload. Verify by looking for the new rule in `document.styleSheets` rather
than trusting the reload — a stale stylesheet looks exactly like a CSS bug.

**A `curl` check proves nothing about the browser.** curl has no cache, so
fetching the bundle URL and grepping for the new rule confirms only that the
*server* is right. Reporting that as "live" is wrong: the tab still holds the
old file.

**The rebundle itself is sound — the client is the only problem.** The loop
verifies its own work: `Get-BundleServingVerdict` HEADs `/dist/bundle.js` after
every rebundle and warns if the server would still hand out a stale copy (a
precompressed `.gz` dotnet-build emitted, or a `Content-Length` from an older
manifest). "Rebundle: the server serves the new bundle" in the log means that
check passed and the bytes really are the new ones.

What is unchanged is the `/dist/bundle.<hash>.*` URL — an ASP.NET
static-web-assets fingerprint computed during `dotnet build` and served
`max-age=31536000, immutable`. So: rebundle when you can reload with caching
disabled (or are driving the page yourself); force a full loop iteration
(`curl /health/stop`, ~60s) when someone else has the tab, because dotnet-build
recomputes the fingerprint and the page then references a genuinely new URL that
no cache can shadow.

See [[voxt-ui-perf-profile-client-first]] for the related "measure, don't assume"
rule on the client side.
