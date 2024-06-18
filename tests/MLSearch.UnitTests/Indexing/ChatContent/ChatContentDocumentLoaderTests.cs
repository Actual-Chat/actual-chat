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

    [Fact]
    public async Task LoadTailMethodProperlyCallsSearchEngine()
    {
        const int tailSize = 333;
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var resultDocuments = CreateSearchResults();
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((_, _) => {
                return Task.FromResult(new SearchResult<ChatSlice>(resultDocuments));
            });
        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy) {
            TailSize = tailSize
        };

        var ctSource = new CancellationTokenSource();
        var cursor = new ChatContentCursor(0, 0);
        var results = await documentLoader.LoadTailAsync(cursor, ctSource.Token);
        Assert.Equal(resultDocuments.Select(rankedDoc => rankedDoc.Document), results);

        searchEngine.Verify(x => x.Find(
            It.Is<SearchQuery>(x => x.Limit == tailSize
                && x.MetadataFilters.Single() is Int64RangeFilter
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
        await Assert.ThrowsAsync<UniqueException>(() => documentLoader.LoadTailAsync(cursor));
    }

    [Fact]
    public async Task LoadByEntryIdsMethodProperlyCallsSearchEngine()
    {
        const int tailSize = 333;
        var namingPolicy = ResolveNamingPolicy(NamingPolicy.CamelCase);
        var resultDocuments = CreateSearchResults();
        var searchEngine = new Mock<ISearchEngine<ChatSlice>>();
        searchEngine
            .Setup(x => x.Find(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
            .Returns<SearchQuery, CancellationToken>((_, _) => {
                return Task.FromResult(new SearchResult<ChatSlice>(resultDocuments));
            });
        var documentLoader = new ChatContentDocumentLoader(searchEngine.Object, namingPolicy) {
            TailSize = tailSize
        };

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

    private static RankedDocument<ChatSlice>[] CreateSearchResults()
    {
        var authorId = new PrincipalId(UserId.New(), AssumeValid.Option);
        var chatId = new ChatId(Generate.Option);
        var entryIds = Enumerable.Range(1, 4)
            .Select(id => new ChatEntryId(chatId, ChatEntryKind.Text, id, AssumeValid.Option))
            .ToArray();
        var textItems = new [] {
            "An accident happend to my brother Jim.",
            "Somebody threw a tomato at him.",
            "Tomatoes are juicy they can't hurt the skin.",
            "But this one was specially packed in a tin.",
        };
        return entryIds.Zip(textItems)
            .Select((args, i) => {
                var (id, text) = args;
                var metadata = new ChatSliceMetadata(
                    [authorId],
                    [new ChatSliceEntry(id, 1, 1)], null, null,
                    [], [], [], [],
                    false,
                    "en-US",
                    DateTime.Now.AddMinutes(-i)
                );
                return new RankedDocument<ChatSlice>(i, new ChatSlice(metadata, text));
            })
            .ToArray();
    }
}
