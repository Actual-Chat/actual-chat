using System.ComponentModel;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using Microsoft.SemanticKernel;


namespace ActualChat.MLSearch.Bot.Services;

internal sealed class SearchToolPlugin(
    IFilters filters,
    ISearchEngine<ChatSlice> searchEngine
)
{
    [KernelFunction]
    [Description("Perform a search for content related to the specified query")]
    public Task<string[]> Find(
        [Description("Type of the search to run")] SearchType searchType,
        [Description("What to search for")] string query
    )
    {
        var results = new string[] {
            $"Dumb {query} content",
            $"Expected {searchType} cotent"
        };
        return Task.FromResult(results);
    }
}
