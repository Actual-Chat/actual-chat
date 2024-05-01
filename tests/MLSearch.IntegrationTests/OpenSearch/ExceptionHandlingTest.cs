
using ActualChat.MLSearch.Engine.OpenSearch;
using ActualChat.MLSearch.Engine.OpenSearch.Extensions;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.IntegrationTests.OpenSearch;

public class ExceptionHandlingTest
{
    [Theory]
//    [InlineData("http://not-a-host:9200")]
    [InlineData("http://localhost:9299")]
    public async Task TheCaseWhenThereIsNoConnectionIsDetectable(string invalidUrl)
    {
        var connectionSettings = new ConnectionSettings(
            new SingleNodeConnectionPool(new Uri(invalidUrl)),
            sourceSerializer: (builtin, settings) => new OpenSearchJsonSerializer(builtin, settings));
        var client = new OpenSearchClient(connectionSettings);
        try {
            var result = await client.Cat.IndicesAsync(cat => cat.Index("*"));
            result.AssertSuccess();
            Assert.Fail("OpenSearch operation must fail.");
        }
        catch (Exception e) when (NoOpenSearchConnection(e)) {
        }
        catch {
            Assert.Fail("Failure detecting OpenSearch connection loss.");
        }
    }

    private bool NoOpenSearchConnection(Exception e)
        => e.InnerException is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError };
}
