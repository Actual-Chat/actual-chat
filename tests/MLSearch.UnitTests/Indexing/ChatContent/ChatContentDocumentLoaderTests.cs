using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.MLSearch.Engine.OpenSearch.Configuration;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public class ChatContentDocumentLoaderTests(ITestOutputHelper @out) : TestBase(@out)
{
    public enum NamingPolicy {
        PascalCase,
        CamelCase,
        SnakeCase,
    }
    private sealed class PascalCasePolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name;
    }

    [Theory]
    [InlineData(NamingPolicy.PascalCase)]
    [InlineData(NamingPolicy.CamelCase)]
    [InlineData(NamingPolicy.SnakeCase)]
    public async Task DocumentLoaderAppliesNamingPolicyToFieldNames(NamingPolicy policy)
    {
        var namingPolicy = ResolveNamingPolicy(policy);
        var expectedIdFieldName = string.Join('.',
            new[] {
                nameof(ChatSlice.Metadata),
                nameof(ChatSliceMetadata.ChatEntries),
                nameof(ChatSliceEntry.Id),
            }.Select(namingPolicy.ConvertName));
        var expectedLocalIdFieldName = string.Join('.',
            new[] {
                nameof(ChatSlice.Metadata),
                nameof(ChatSliceMetadata.ChatEntries),
                nameof(ChatSliceEntry.LocalId),
            }.Select(namingPolicy.ConvertName));

        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        var isIdFieldNameExpected = default(bool?);
        var isLocalIdFieldNameExpected = default(bool?);
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((q, _) => {
                if (q.MetadataFilters.First() is Int64RangeFilter rangeFilter) {
                    isLocalIdFieldNameExpected ??= true;
                    isLocalIdFieldNameExpected &= expectedLocalIdFieldName.Equals(rangeFilter.FieldName, StringComparison.Ordinal);
                    isLocalIdFieldNameExpected &= expectedLocalIdFieldName.Equals(q.SortStatements?[0].Field, StringComparison.Ordinal);
                }
                else if (q.MetadataFilters.First() is OrFilter orFilter) {
                    isIdFieldNameExpected ??= true;
                    isIdFieldNameExpected &= orFilter.Filters.Cast<EqualityFilter<ChatEntryId>>().All(
                        f => expectedIdFieldName.Equals(f.FieldName, StringComparison.Ordinal));
                }
                return Task.FromResult(new SearchResult<ChatSlice>([]));
            });

        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy);

        var cursor = new ChatContentCursor(0, 0);
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 101, AssumeValid.Option);
        _ = await documentLoader.LoadTailAsync(cursor);
        _ = await documentLoader.LoadByEntryIdsAsync([chatEntryId]);
        Assert.True(isIdFieldNameExpected);
        Assert.True(isLocalIdFieldNameExpected);
    }

    private static OpenSearchNamingPolicy ResolveNamingPolicy(NamingPolicy policy) => new(policy switch {
        NamingPolicy.PascalCase => new PascalCasePolicy(),
        NamingPolicy.CamelCase => JsonNamingPolicy.CamelCase,
        NamingPolicy.SnakeCase => JsonNamingPolicy.SnakeCaseLower,
        _ => throw new NotSupportedException(),
    });
}
