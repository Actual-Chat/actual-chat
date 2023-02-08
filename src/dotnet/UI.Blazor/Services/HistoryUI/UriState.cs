namespace ActualChat.UI.Blazor.Services;

public sealed record UriState(string Uri) : HistoryState
{
    public override double Priority => double.PositiveInfinity;
    public override int BackCount => 0;

    public UriState(NavigationManager nav) : this(nav.GetLocalUrl().Value)
    { }

    public override string ToString()
        => $"{GetType().Namespace}('{Uri}')";

    public override HistoryState Apply(HistoryChange change)
        => throw StandardError.Internal($"Apply shouldn't ever be called on {GetType().Name}.");

    // "With" helpers

    public UriState With(string uri)
        => OrdinalEquals(Uri, uri) ? this : new UriState(uri);
}
