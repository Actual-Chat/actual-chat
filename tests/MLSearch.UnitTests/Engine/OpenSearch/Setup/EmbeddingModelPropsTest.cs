using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch.Setup;

public class EmbeddingModePropsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string ModelId1 = "id_model_qwerty_1";
    private const string ModelId2 = "id_model_qwerty_2";
    private const int EmbeddingDimension1 = 1001;
    private const int EmbeddingDimension2 = 1002;
    private const string ModelAllConfig1 = "__SOME_CONTENT__1__";
    private const string ModelAllConfig2 = "__SOME_CONTENT__2__";

    [Theory]
    [MemberData(nameof(ModelProps))]
    public void PropertyValuesAssignedAsExpected(string modelId, int embeddingDimension, string allConfig)
    {
        var props = new EmbeddingModelProps(modelId, embeddingDimension, allConfig);
        Assert.Equal(modelId, props.Id);
        Assert.Equal(embeddingDimension, props.EmbeddingDimension);
    }

    public static TheoryData<string, int, string> ModelProps => new() {
        {ModelId1, EmbeddingDimension1, ModelAllConfig1},
        {ModelId2, EmbeddingDimension2, ModelAllConfig2},
    };

    [Fact]
    public void UniqueKeyDependsOnAllConfig()
    {
        var props10 = new EmbeddingModelProps("", 0, ModelAllConfig1);
        var props11 = new EmbeddingModelProps("", 0, ModelAllConfig1);
        var props2 = new EmbeddingModelProps("", 0, ModelAllConfig2);

        Assert.Equal(props10.UniqueKey, props11.UniqueKey);
        Assert.NotEqual(props10.UniqueKey, props2.UniqueKey);
    }

    [Fact]
    public void UniqueKeyDoesNotDependOnIdAndDimension()
    {
        const string SameConfig = ModelAllConfig1;
        var props10 = new EmbeddingModelProps(ModelId1, EmbeddingDimension1, SameConfig);
        var props11 = new EmbeddingModelProps(ModelId2, EmbeddingDimension2, SameConfig);

        Assert.Equal(props10.UniqueKey, props11.UniqueKey);
    }
}
