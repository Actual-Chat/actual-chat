using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.Bot.Services;

internal class SearchBotPluginSet(
    ISearchPlugin searchPlugin,
    IForwardPlugin forwardPlugin
)
{
    public IReadOnlyCollection<KernelPlugin> Plugins { get; } = [
        searchPlugin.ToKernelPlugin(),
        forwardPlugin.ToKernelPlugin(),
    ];
}

internal static class SearchBotPluginSetExtensions
{
    public static KernelPlugin ToKernelPlugin(this ISearchPlugin searchPlugin)
        => KernelPluginFactory.CreateFromFunctions(PluginNames.SearchPlugin, functions: CreatePluginFunctions(searchPlugin));
    private static IEnumerable<KernelFunction>? CreatePluginFunctions(ISearchPlugin searchPlugin)
    {
        [Description("Perform a search for content related to the specified query")]
        Task<SearchResult[]> Find(
            [Description("What to search for.")] string queryText,
            [Description("Type of the search to run.")] SearchType searchType,
            [Description("ID of ongoing search conversation.")] string conversationId,
            [Description("ID of the user who runs the search.")] string userId,
            [Description("Limit to the number of returned results.")] int limit = 1,
            CancellationToken cancellationToken = default
        )
            => searchPlugin.Find(queryText, searchType, conversationId, userId, limit, cancellationToken);

        var findFunction = KernelFunctionFactory.CreateFromMethod(Find, null);
        return [findFunction];
    }

    public static KernelPlugin ToKernelPlugin(this IForwardPlugin forwardPlugin)
        => KernelPluginFactory.CreateFromFunctions(PluginNames.ForwardPlugin, functions: CreatePluginFunctions(forwardPlugin));
    private static IEnumerable<KernelFunction>? CreatePluginFunctions(IForwardPlugin forwardPlugin)
    {
        [Description("Forward last search results to the user with a summary.")]
        Task ForwardResults(
            [Description("Search results summary.")] string summary,
            [Description("List of links to the relevant results.")] IReadOnlyList<string> links,
            [Description("ID of ongoing search conversation.")] string conversationId,
            CancellationToken cancellationToken = default
        )
            => forwardPlugin.ForwardResults(summary, links, conversationId, cancellationToken);

        var findFunction = KernelFunctionFactory.CreateFromMethod(ForwardResults, null);
        return [findFunction];
    }
}
