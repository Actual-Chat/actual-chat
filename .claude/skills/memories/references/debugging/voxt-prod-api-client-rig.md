`tmp/api-client` (modelled on `src/dotnet/App.ConsoleClient`) calls prod's Fusion
RPC services directly: `dotnet run --project tmp/api-client/api-client.csproj`,
`-h:dev.voxt.ai` to switch instance.

The parts that aren't obvious:
- A session id is just a string; `ActualChat.SessionExt.NewApiKey()` mints a
  valid `!`-prefixed one locally. Methods that take no `Session` (e.g.
  `IAppUpdates.GetLatestUpdateInfo`) need no real account at all.
- The RPC connection wants `HostInfo.HostKind = MauiApp` — that's what enables
  the WebSocket client that sends the session header.
- `ActualChat.SessionExt` collides with `ActualLab.Fusion.SessionExt`; qualify it.
- A csproj under `tmp/` inherits the root `Directory.Build.props` (net11.0,
  implicit usings), so it only needs `OutputType` plus a reference to
  `Api.Contracts`.

Calling a `[ComputeMethod]` from here has a side effect worth knowing: it runs
the server's compute path, which can kick off server-side work that no real
client had triggered — asking for `AppKind.Windows` was what first made prod
probe the Microsoft Store at all.
