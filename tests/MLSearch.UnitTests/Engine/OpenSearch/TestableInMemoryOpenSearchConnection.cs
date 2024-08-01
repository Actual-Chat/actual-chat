using System.Text;
using OpenSearch.Net;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch;

internal sealed class TestableInMemoryOpenSearchConnection(Action<RequestData> perRequestAssertion, List<(int, string)> responses) : IConnection
{
    internal static readonly byte[] EmptyBody = Encoding.UTF8.GetBytes("");
    private int _requestCounter = -1;

    public void AssertExpectedCallCount() => _requestCounter.Should().Be(responses.Count - 1);

    async Task<TResponse> IConnection.RequestAsync<TResponse>(RequestData requestData, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCounter);

        perRequestAssertion(requestData);

        await Task.Yield(); // avoids test deadlocks

        var (statusCode, response) = responses.Count > _requestCounter
            ? responses[_requestCounter]
            : (500, string.Empty);

        var stream = !string.IsNullOrEmpty(response)
            ? requestData.MemoryStreamFactory.Create(Encoding.UTF8.GetBytes(response))
            : requestData.MemoryStreamFactory.Create(EmptyBody);

        return await ResponseBuilder
            .ToResponseAsync<TResponse>(requestData, null, statusCode, null, stream, RequestData.MimeType, cancellationToken)
            .ConfigureAwait(false);
    }

    TResponse IConnection.Request<TResponse>(RequestData requestData)
    {
        Interlocked.Increment(ref _requestCounter);

        perRequestAssertion(requestData);

        var (statusCode, response) = responses.Count > _requestCounter
            ? responses[_requestCounter]
            : (500, string.Empty);

        var stream = !string.IsNullOrEmpty(response)
            ? requestData.MemoryStreamFactory.Create(Encoding.UTF8.GetBytes(response))
            : requestData.MemoryStreamFactory.Create(EmptyBody);

        return ResponseBuilder.ToResponse<TResponse>(requestData, null, statusCode, null, stream, RequestData.MimeType);
    }

    public void Dispose() { }
}
