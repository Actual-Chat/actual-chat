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
        var expectedChatIdFieldName = string.Join('.',
            new[] {
                nameof(ChatSlice.Metadata),
                nameof(ChatSliceMetadata.ChatId),
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
        var isChatIdFieldNameExpected = default(bool?);
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((q, _) => {
                if (q.MetadataFilters.Where(f => f is Int64RangeFilter).SingleOrDefault() is Int64RangeFilter rangeFilter) {
                    isLocalIdFieldNameExpected ??= true;
                    isLocalIdFieldNameExpected &= expectedLocalIdFieldName.Equals(rangeFilter.FieldName, StringComparison.Ordinal);
                    isLocalIdFieldNameExpected &= expectedLocalIdFieldName.Equals(q.SortStatements?[0].Field, StringComparison.Ordinal);
                }
                if (q.MetadataFilters.Where(f => f is EqualityFilter<string>).SingleOrDefault() is EqualityFilter<string> chatIdFilter) {
                    isChatIdFieldNameExpected ??= true;
                    isChatIdFieldNameExpected &= expectedChatIdFieldName.Equals(chatIdFilter.FieldName, StringComparison.Ordinal);
                }
                if (q.MetadataFilters.Where(f => f is OrFilter).SingleOrDefault() is OrFilter orFilter) {
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
        _ = await documentLoader.LoadTailAsync(chatId, cursor, 5);
        _ = await documentLoader.LoadByEntryIdsAsync([chatEntryId]);
        Assert.True(isIdFieldNameExpected);
        Assert.True(isLocalIdFieldNameExpected);
        Assert.True(isChatIdFieldNameExpected);
    }

    [Fact]
    public async Task LoadTailMethodProperlyCallsSearchEngine()
    {
        const int tailSetSize = 333;
        const long lastLocalId = 9999;
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var resultDocuments = CreateSearchResults();
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((_, _) => {
                return Task.FromResult(new SearchResult<ChatSlice>(resultDocuments));
            });
        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy);

        var chatId = new ChatId(Generate.Option);
        var cursor = new ChatContentCursor(0, lastLocalId);
        var ctSource = new CancellationTokenSource();
        var results = await documentLoader.LoadTailAsync(chatId, cursor, tailSetSize, ctSource.Token);
        Assert.Equal(resultDocuments.Select(rankedDoc => rankedDoc.Document), results);

        searchEngine.Verify(x => x.Find(
            It.Is<SearchQuery>(x => x.Limit == tailSetSize
                && x.MetadataFilters
                    .Where(f => f is EqualityFilter<string>)
                    .Cast<EqualityFilter<string>>()
                    .Where(f => f.Value.Equals(chatId, StringComparison.Ordinal))
                    .Single() != null
                && x.MetadataFilters
                    .Where(f => f is Int64RangeFilter)
                    .Cast<Int64RangeFilter>()
                    .Where(f => !f.From.HasValue && f.To.HasValue && f.To.Value.Value == lastLocalId && f.To.Value.Include)
                    .Single() != null
                && x.SortStatements![0].SortOrder == QuerySortOrder.Descenging),
            It.Is<CancellationToken>(x => x == ctSource.Token)
        ));
    }

    [Fact]
    public async Task LoadTailMethodDoesntSwallowExceptions()
    {
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<SearchResult<ChatSlice>>(new UniqueException()));

        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy);

        var cursor = new ChatContentCursor(0, 0);
        await Assert.ThrowsAsync<UniqueException>(() => documentLoader.LoadTailAsync(ChatId.None, cursor, 5));
    }

    [Fact]
    public async Task LoadByEntryIdsMethodProperlyCallsSearchEngine()
    {
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var resultDocuments = CreateSearchResults();
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((_, _) => {
                return Task.FromResult(new SearchResult<ChatSlice>(resultDocuments));
            });
        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy);

        var ctSource = new CancellationTokenSource();
        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 101, AssumeValid.Option);
        var results = await documentLoader.LoadByEntryIdsAsync([chatEntryId], ctSource.Token);
        Assert.Equal(resultDocuments.Select(rankedDoc => rankedDoc.Document), results);

        searchEngine.Verify(x => x.Find(
            It.Is<SearchQuery>(x => x.MetadataFilters.Single() is OrFilter),
            It.Is<CancellationToken>(x => x == ctSource.Token)
        ));
    }

    [Fact]
    public async Task LoadByEntryIdsMethodDoesntSwallowExceptions()
    {
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromException<SearchResult<ChatSlice>>(new UniqueException()));

        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy);

        var chatId = new ChatId(Generate.Option);
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 101, AssumeValid.Option);
        await Assert.ThrowsAsync<UniqueException>(() => documentLoader.LoadByEntryIdsAsync([chatEntryId]));
    }

    private static OpenSearchNamingPolicy ResolveNamingPolicy(NamingPolicy policy) => new(policy switch {
        NamingPolicy.PascalCase => new PascalCasePolicy(),
        NamingPolicy.CamelCase => JsonNamingPolicy.CamelCase,
        NamingPolicy.SnakeCase => JsonNamingPolicy.SnakeCaseLower,
        _ => throw new NotSupportedException(),
    });

    private static RankedDocument<ChatSlice>[] CreateSearchResults() => ChatContentTestHelpers.CreateDocuments()
        .Select((doc, i) => new RankedDocument<ChatSlice>(i, doc))
        .ToArray();
}
