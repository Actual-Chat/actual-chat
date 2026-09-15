# Infrastructure and production quirks

Each entry is a couple of sentences. The full write-up — commands, ids, evidence — is in
`../references/infra/<name>.md`; open it when the one-liner turns out to matter.

## Deployment

- **Env vars and secrets are owned by Flux.** Edit the dev or prod branch of
  `ActualChat-flux-team-core`; a `kubectl patch` on `actual-chat-app-secret` gets reverted. Keys
  are base64 with CRLF, and a new key should use an optional `secretKeyRef`.
  → `../references/infra/dev-env-vars-via-flux-team-core.md`
- **Prod used to pin OpenAI model names in Flux**, so a code-default model change could be tested
  on dev and still break prod — this caused the v2.20 translation outage on 2026-09-11. Diff the
  dev and prod model env vars before any model change.
  → `../references/infra/prod-pins-openai-models-in-flux.md`
- **App pods never receive SIGTERM** (the entrypoint is `sh -c`), so every rollout is a hard kill
  plus a 22 s mesh-lease expiry. → `../references/infra/gke-rollout-kills-pods-hard.md`

## Reaching the clusters

- **`kubectl` works without `gke-gcloud-auth-plugin`** by passing `--token` from gcloud.
  → `../references/infra/kubectl-via-gcloud-token.md`
- **CPU traces, live thread stacks and heap dumps from a prod pod** come from the dotnet-monitor
  sidecar plus `dotnet-dump` (.NET 11). → `../references/infra/prod-live-diagnostics.md`
- Prod and dev **log** search have their own skills: `/gcloud-prod-logs`, `/gcloud-dev-logs`.

## External consoles

- **Play Console ANR list**: the default "user-perceived" chip hides roughly 70% of ANRs, and
  pagination is a material-button, not a link. → `../references/infra/play-console-anr-browsing.md`
- **Map a Play version back to a commit**: `versionCode = 65536 × minorIndex + nbgv height`, and
  the shipped build is usually not the branch tip. → `../references/infra/play-version-to-commit-mapping.md`
- **Sentry covers client-side errors only**, and several of its endpoints 403 for our token;
  `statsPeriod` only accepts 24h/14d. → `../references/infra/sentry-api-access.md`
- **The App Store update probe reads a ~24 h Akamai-cached iTunes lookup**, so iOS/macOS update
  detection can lag a release by most of a day — `AnnounceDelay` does not cover this.
  → `../references/infra/itunes-lookup-akamai-24h-cache.md`
- **Soniox enforces two org-wide caps** — stored artifacts and ~500 req/min shared with live
  traffic. Use `/soniox-sweep`. → `../references/infra/soniox-org-file-cap.md`
- **Prod logs and Play Console reports over gcloud**, including the non-interactive reauth recipe
  and the gzip/UTF-16 CSV decode: `/production-data-access`.
  → `../references/infra/gcp-prod-and-play-store-access.md`
