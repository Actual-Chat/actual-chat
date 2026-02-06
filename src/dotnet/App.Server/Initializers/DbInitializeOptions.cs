namespace ActualChat.App.Server.Initializers;

/// <summary>
/// Options controlling which database initialization phases run at startup.
/// </summary>
public sealed record DbInitializeOptions
{
    public static readonly DbInitializeOptions Default = new();

    public bool InitializeData { get; init; } = true;
}
