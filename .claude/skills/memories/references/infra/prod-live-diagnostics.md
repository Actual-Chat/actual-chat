Prod pods (`actual-chat-app-*`, project `actual-chat-app-prod`, cluster `k8s-cluster` in `us-central1-a`) run a
`dotnet-monitor` sidecar (`--no-auth`, HTTP on port 54323). Its `HighCpu` collection rule (cpu-usage > 200%,
once per 4h) writes 30s CPU `.nettrace` files to `gs://actual-chat-app-prod-dumps/`; tiny (2-5 KB) files there
are aborted collections, only the 100+ MB ones are real.

Recipe used on 2026-09-02 (all read-only for the app; a dump pauses it for seconds):
- kubectl context `gke_actual-chat-app-prod_us-central1-a_k8s-cluster` exists; it needs
  `gcloud components install gke-gcloud-auth-plugin` (installed on 2026-09-02).
- `kubectl port-forward pod/<pod> 54323:54323`, then `curl localhost:54323/processes`,
  `/dump?type=Mini` (~90 MB, ~1s pause, stacks only) or `/dump?type=WithHeap` (~2.6 GB, ~4s pause, strings readable).
  `/stacks` is NOT enabled (in-process features off).
- Prod runs .NET 11 preview: released `dotnet-dump` 9.x fails with "CLR debugging layer reported a version of 10".
  Install a 10.x prerelease from the dnceng `dotnet-tools` feed with a standalone nuget.config
  (`dotnet tool update -g dotnet-dump --prerelease --configfile <that config>`); run it from outside the repo,
  the repo's package source mapping blocks `--add-source`.
- `setthread -t <decimal tid>` (hex is rejected), `clrstack -all`, `dumpheap -type <Type>`, `dumpobj`.
  `dumpobj` prints an empty `String:` for long strings; read them from the ELF core directly in Python
  (map VA via PT_LOAD headers, length at +8, UTF-16 chars at +12).
- `dotnet-trace convert --format speedscope` on the nettrace gives per-thread stacks, but EventPipe
  truncates deep recursions (~105 frames) so callers of a deep parse are lost - use a dump for those.

**Why:** the first prod CPU investigation took several detours (wrong tool version, disabled endpoints).
**How to apply:** for a prod CPU/hang question, start from the GCS nettrace, then go straight to a Mini dump.
Delete dumps after use - they contain user message text. See [[markup-parser-exponential-backtracking]].
