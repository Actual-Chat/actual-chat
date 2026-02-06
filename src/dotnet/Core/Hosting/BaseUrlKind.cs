namespace ActualChat.Hosting;

/// <summary>
/// Specifies the environment type for base URL configuration.
/// </summary>
public enum BaseUrlKind
{
    Unknown = 0,
    Production,
    Development,
    Local,
}
