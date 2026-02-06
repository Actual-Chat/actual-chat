namespace ActualChat.Security;

/// <summary>
/// Specifies the format used for session identifiers.
/// </summary>
public enum SessionFormat
{
    Id = 0,
    Token,
    IdOrToken,
}
