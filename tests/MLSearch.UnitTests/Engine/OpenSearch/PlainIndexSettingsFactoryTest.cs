using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch;

public class PlainIndexSettingsFactoryTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string ModelAllConfig = "__SOME_CONTENT__";
    private const string ModelId = "id_model_qwerty";
    private const int EmbeddingDimension = 1024;

    private readonly IndexNames _indexNames = new();
    private readonly ClusterSetupResult _setupResult =
        new(new EmbeddingModelProps(ModelId, EmbeddingDimension, ModelAllConfig));

    [Fact]
    public void SettingsPropertiesSetAsExpected()
    {
        const string indexName = "test-index";
        var factory = new PlainIndexSettingsFactory(_indexNames, MockClusterSetup());
        var settings = factory.Create(indexName);
        Assert.Contains(indexName, settings.IndexName, StringComparison.Ordinal);
    }

    [Fact]
    public void NullOrEmptyIndexNameIsNotAllowed()
    {
        var factory = new PlainIndexSettingsFactory(_indexNames, MockClusterSetup());
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        Assert.Throws<ArgumentNullException>(() => factory.Create(null));
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        Assert.Throws<ArgumentException>(() => factory.Create(string.Empty));
    }

    private IClusterSetup MockClusterSetup()
    {
        var clusterSetupMock = new Mock<IClusterSetup>();
        clusterSetupMock.SetupGet(clusterSetup => clusterSetup.Result).Returns(_setupResult);
        var clasterSetup = clusterSetupMock.Object;
        return clasterSetup;
    }
}
