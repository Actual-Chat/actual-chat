On 2026-09-03 the running `server-loop.ps1` was launched from `D:\Projects\ActualChat-C1`,
a separate clone (not a git worktree of the main repo), on its own branch. `tmp/server-loop.log`
in the main repo was a day stale while `D:\Projects\ActualChat-C1\tmp\server-loop.log` was live.

**Why:** a `/health/stop` from the main repo still restarts that loop, so the logs you watch and
the branch you edit can silently be the wrong ones.

**How to apply:** before trusting `tmp/server-loop*.log`, find the owner of port 7080
(`Get-NetTCPConnection -LocalPort 7080 -State Listen` then `Get-CimInstance Win32_Process`
for its command line) and the `pwsh ... server-loop.ps1` process path. To test a branch there:
commit + push it, `git -C D:\Projects\ActualChat-C1 fetch/checkout` it (the clone was clean),
put test-only config into `src/dotnet/App.Server/appsettings.local.json` of *that* clone
(gitignored, read at startup), restart via `/health/stop`, and restore the clone's original
branch and remove the settings file when done. The wait recipe is in
[[server-loop-iteration-gotchas]].
