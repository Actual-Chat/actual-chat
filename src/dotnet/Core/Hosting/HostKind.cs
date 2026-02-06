namespace ActualChat.Hosting;

/// <summary>
/// Defines the type of host: server, WASM app, or MAUI app.
/// </summary>
public enum HostKind
{
    Unknown = 0,
    Server,
    WasmApp,
    MauiApp,
}
