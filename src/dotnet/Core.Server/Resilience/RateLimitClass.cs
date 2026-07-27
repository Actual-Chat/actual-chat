namespace ActualChat.Resilience;

/// <summary>
/// Selects the set of budgets an inbound call is charged against.
/// </summary>
public enum RateLimitClass
{
    HttpRead = 0,
    Command,
    Auth,
    SessionCreation,
    GifProvider,
    UploadCreation,
    UploadBytes,
}
