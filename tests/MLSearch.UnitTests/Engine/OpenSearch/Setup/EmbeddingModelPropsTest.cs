using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.UnitTests.Engine.OpenSearch.Setup;

public class EmbeddingModePropsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string ModelId1 = "id_model_qwerty_1";
    private const string ModelId2 = "id_model_qwerty_2";
    private const int EmbeddingDimension1 = 1001;
    private const int EmbeddingDimension2 = 1002;
    private const string UniqueModelKey1 = "__SOME_UNIQUE_KEY__1__";
    private const string UniqueModelKey2 = "__SOME_UNIQUE_KEY__2__";

    [Theory]
    [MemberData(nameof(ModelProps))]
    public void PropertyValuesAssignedAsExpected(string modelId, int embeddingDimension, string uniqueKey)
    {
        var props = new EmbeddingModelProps(modelId, embeddingDimension, uniqueKey);
        Assert.Equal(modelId, props.Id);
        Assert.Equal(embeddingDimension, props.EmbeddingDimension);
    }

    public static TheoryData<string, int, string> ModelProps => new() {
        {ModelId1, EmbeddingDimension1, UniqueModelKey1},
        {ModelId2, EmbeddingDimension2, UniqueModelKey2},
    };
}
