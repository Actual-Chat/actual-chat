namespace ActualChat.Hosting;

/// <summary>
/// Defines the target platform for client applications.
/// </summary>
public enum AppKind
{
    Unknown = 0,
    Wasm,
    Android,
    Ios,
    Windows,
    MacOS,
}
