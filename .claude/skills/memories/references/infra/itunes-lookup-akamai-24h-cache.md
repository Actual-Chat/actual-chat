`itunes.apple.com/lookup` answers with `Cache-Control: max-age=86070` behind
Akamai: the first request to an edge is `TCP_MISS`, every later one is `TCP_HIT`
for ~24 hours. `AppleStoreProbe` sends a plain GET, so a prod pod keeps reading
whatever its us-central1 edge cached when probing started that day — verified
2026-09-08, when the App Store had 2.19.147 (released 21:37 UTC) and prod still
reported 2.18.293 two hours and a dozen probes later, with no exception and no
"isn't listed" log.

`AnnounceDelay` (1 hour) was sized for storefront propagation and does not cover
this. A `Cache-Control: no-cache` request header does NOT make Akamai
revalidate - only a new cache key does, so `StoreProbe.Fetch` now appends a
`_=<guid>` cache buster to every probe URL (all three stores ignore it).
Written uncommitted on `dev` 2026-09-08 along with a second fix: the probe used
to record a store version only when it reached the *server's* build, which
skipped a release as soon as prod deployed on top of it - it now compares
against the record, and `X.Y` train comparison is only the "stop probing" test.

Related: [[voxt-prod-api-client-rig]].
