Investigated on 2026-09-02 (dev rollout of 2.18.221 at 17:14Z). The `actual-chat-app`
Deployment (dev and prod) runs the container as
`/bin/sh -c "update-ca-certificates && dotnet ActualChat.App.Server.dll"`. `sh` is PID 1
without a SIGTERM handler, so the signal is dropped: the .NET host never sees
ApplicationStopping, keeps serving for the whole 30 s grace period, then gets SIGKILL.
Consequences seen in logs: mesh node lease expires 22 s after the kill
(`MeshLockOptions.ReleaseMeshWatcher`), survivors keep dialing the dead endpoint with
Exp(1, 10) backoff until then, and new pods announce into the mesh ~9 s before Kestrel listens.
dev strategy is maxSurge 1 / maxUnavailable 1; prod is maxSurge 1 / maxUnavailable 0.

**Why:** Alex wants live audio/video to survive server restarts with the same two pods.
**How to apply:** Any graceful-shutdown work in App.Server is dead code until the manifest
uses `exec dotnet ...` (manifest lives in flux-team-core, see [[kubectl-via-gcloud-token]]).

Status 2026-09-02: code side done in PR #4346 (`feat/graceful-restart`, worktree
`D:\Projects\ActualChat-C1-graceful-restart`) paired with Fusion master commit d6866aa40
(503 gate, `MustRejectOnApplicationStopping`); Fusion not yet published, manifest `exec` fix
still pending in flux-team-core. Alex vetoed shortening the mesh lease (GC pauses).
