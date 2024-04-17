namespace ActualChat.App.Server.Initializers;

public sealed record DbInitializeOptions
{
    public static readonly DbInitializeOptions Default = new();

    public bool InitializeData { get; init; } = true;
}
