The GKE app deployment (`default/actual-chat-app`) is managed by Flux from `github.com/actual-chat/flux-team-core`:
dev cluster syncs branch `dev`, prod cluster syncs branch `prod` (branches diverge; not a mirror).
Local checkout: `D:\Projects\ActualChat-flux-team-core` (usually sitting on `prod`, often far behind) —
use a throwaway `git worktree` on `origin/dev` instead of switching it.

- Manifests: `clusters/actual-chat/default/actual-chat-app/actual-chat-app-deployment.yaml` (env block) and
  `actual-chat-app-secret.yaml` (plain base64 `data:`, no SOPS; CRLF line endings — insert lines preserving CRLF).
- The Secret carries Flux kustomize labels, so a `kubectl patch` is reverted on reconcile; commit to the repo.
- Pattern: secret key + `valueFrom.secretKeyRef`; add `optional: true` for env-specific keys so the other env's pods still start.
- No `kustomization.yaml` in that dir (Flux generates one), so `kubectl kustomize` fails; validate with
  `kubectl apply --dry-run=client --validate=false -o name -f <file>`.
- Flux applies within ~2 min of a push (GitRepository interval 1m); verify via the kustomization's
  `lastAppliedRevision`, then `kubectl rollout status deploy/actual-chat-app`.
- Push straight to `dev`; the only other committer there is image automation ("Bump docker image versions.").
- `prod` has a "changes must be made through a pull request" rule; Alex's account bypasses it on push (GitHub
  prints "Bypassed rule violations"). Push to prod only with explicit approval.
- `core.autocrlf=true`: blobs are LF and only the working copy is CRLF, so `sed -i` on
  `actual-chat-app-deployment.yaml` commits a clean diff. Check `git show <rev>:<file> | tr -cd '\r' | wc -c`
  before assuming a file is CRLF in the repo.
- Kustomization `lastAppliedRevision` updates only after the rollout is healthy, so `rollout status` right after it returns at once.
- Prod model pins caused the v2.20 translation outage: [[prod-pins-openai-models-in-flux]].

First used 2026-09-10 for `UsersSettings__PredefinedEmailTotps__npc` (issue #4479).
