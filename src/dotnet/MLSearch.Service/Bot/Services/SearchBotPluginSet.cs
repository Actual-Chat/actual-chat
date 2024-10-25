using Microsoft.SemanticKernel;

namespace ActualChat.MLSearch.Bot.Services;

internal interface ISearchBotPluginSet
{
    IReadOnlyCollection<KernelPlugin> Plugins { get; }
}

internal class SearchBotPluginSet(IServiceProvider serviceProvider): ISearchBotPluginSet
{
    public IReadOnlyCollection<KernelPlugin> Plugins { get; } = [
        KernelPluginFactory.CreateFromType<SearchPlugin>(nameof(SearchPlugin), serviceProvider),
        KernelPluginFactory.CreateFromType<ForwardPlugin>(nameof(ForwardPlugin), serviceProvider),
    ];
}
