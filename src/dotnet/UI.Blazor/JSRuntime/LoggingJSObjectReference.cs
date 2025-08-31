namespace ActualChat.UI.Blazor;

public class LoggingJSObjectReference(IJSObjectReference source, string name, ILogger log) : IJSObjectReference
{
    private const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties;

    public IJSObjectReference Source { get; } = source;
    public string Name { get; } = name;
    public ILogger Log { get; } = log;

    public ValueTask DisposeAsync()
        => Source.DisposeAsync();

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, object?[]? args)
    {
        try {
            return await Source.InvokeAsync<TValue>(identifier, args).ConfigureAwait(true);
        }
        catch (Exception e) {
            Log.LogError(e, "{Name}.{Identifier} call failed", Name, identifier);
            throw;
        }
    }

    public async ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        try {
            return await Source.InvokeAsync<TValue>(identifier, cancellationToken, args).ConfigureAwait(true);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "{Name}.{Identifier} call failed", Name, identifier);
            throw;
        }
    }
}
