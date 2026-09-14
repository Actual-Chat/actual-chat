When `ActualLabFusionVersion` is bumped in Directory.Packages.props (seen on 13.0.163 → 14.0.17, 2026-07-17), the server builds and runs fine but the **WASM client crashes at entrypoint** in a reload loop with `Your mono runtime and class libraries are out of sync. The out of sync library is: System.Private.CoreLib.dll` (console shows only `(null)` / `Unhandled Exception` via the chrome-devtools MCP; the real text needs a Playwright console tap or the user's own devtools).

**Why:** stale incremental build outputs mix old/new framework assemblies in the served `_framework` payload; regenerated AOT sources alone don't fix it.

**How to apply:** after the bump, (1) run `./update-aot-helpers.cmd` and commit the regenerated `*AotSource.g.cs` files, (2) delete `artifacts/bin` and let server-loop rebuild clean, (3) hard-reload browsers with SW/cache cleanup, then verify in WASM render mode (`debugUI.setRenderMode('w')`) — server mode ('s') working proves nothing about WASM.
