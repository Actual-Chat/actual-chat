namespace ActualChat.UI.Blazor.Diagnostics;

public sealed class ConditionalPropagatorHandler : DelegatingHandler
{
    public ConditionalPropagatorHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ignoreRequest = ConditionalPropagator.IgnoreRequest.Value;
        try {
            ConditionalPropagator.IgnoreRequest.Value = true;
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally {
            ConditionalPropagator.IgnoreRequest.Value = ignoreRequest;
        }
    }
}
