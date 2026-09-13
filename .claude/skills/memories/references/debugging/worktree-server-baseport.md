To run App.Server from a worktree while the main checkout's server holds port 7080, set
`HostSettings__BasePort=<port>` (e.g. 7081) — see `AppHost.Build.cs`, which builds the server
URL and the mesh endpoint from it. Plain `ASPNETCORE_URLS` is NOT enough: the app still
announces `localhost:7080` as its MeshNode endpoint (from config), binds 127.0.0.1:7080
alongside the main server's 0.0.0.0:7080 (Windows allows specific-IP bind next to wildcard,
hijacking loopback traffic), and the mesh duplicate-endpoint protocol then kills one instance
via `/health/stop`.

Both servers share Postgres/Redis/NATS and form a 2-node mesh; this is the sanctioned flow
(the BasePort comment says "e.g. from .env for worktrees"). Stop the worktree server with
`curl http://127.0.0.1:<port>/health/stop`.

On such a bare `dotnet run` server the predefined-phone TOTP bypass (`+1 555 555 555x`) is
not configured — `debugUI.signIn('test-1@actual.chat')` works instead, but expect a
"Register new account?" confirm dialog on a fresh DB (click Register).

Gotcha met on the way: `dotnet build ... | tail` masks the exit code — check `PIPESTATUS[0]`,
or the earlier "build succeeded" may be false.
