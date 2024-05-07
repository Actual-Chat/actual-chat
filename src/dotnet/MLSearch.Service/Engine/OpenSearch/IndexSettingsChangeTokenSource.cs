using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal interface IIndexSettingsChangeTokenSource : IOptionsChangeTokenSource<IndexSettings>
{
    void RaiseChanged();
}

internal class IndexSettingsChangeTokenSource(string name) : IIndexSettingsChangeTokenSource
{
    private ConfigurationReloadToken _changeToken = new();
    public string Name => name;
    public IChangeToken GetChangeToken() => _changeToken;

    public void RaiseChanged()
    {
        ConfigurationReloadToken previousToken = Interlocked.Exchange(ref _changeToken, new ConfigurationReloadToken());
        previousToken.OnReload();
    }
}
