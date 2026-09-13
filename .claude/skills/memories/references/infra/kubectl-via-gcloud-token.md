`gke-gcloud-auth-plugin` isn't installed on this machine, so plain `kubectl` against
`k8s-cluster` (dev project `actual-chat-app-dev`, prod project `actual-chat-app-prod`,
zone `us-central1-a`) fails. Workaround that works in Git Bash:

```
gcloud container clusters get-credentials k8s-cluster --zone us-central1-a --project actual-chat-app-dev
TOKEN=$(gcloud auth print-access-token); kubectl --token="$TOKEN" get deploy actual-chat-app -o yaml
```

The K8s manifests are NOT in this repo or in `ActualChat-k8s-dev`; Flux pulls them from
`ssh://git@github.com/actual-chat/flux-team-core.git` (branch `dev` for dev).
Related: [[gke-rollout-kills-pods-hard]].

The flux repo is now cloned at `D:\Projects\ActualChat-flux-team-core`. Branches: `dev` -> dev
cluster, `prod` -> prod cluster (merged from dev), `master`/`main` are stale. Flux image
automation commits "Bump docker image versions." straight to `dev`/`prod`. Neither `gh`'s
GH_TOKEN nor the GitHub MCP can see that repo (404), so PRs there must be opened by hand;
`git push` over SSH works.
