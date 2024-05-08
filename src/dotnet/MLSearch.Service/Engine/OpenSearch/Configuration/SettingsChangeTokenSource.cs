using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal interface ISettingsChangeTokenSource
{
    void RaiseChanged();
}

internal class SettingsChangeTokenSource<TSettings>(string name)
    : ISettingsChangeTokenSource, IOptionsChangeTokenSource<TSettings>
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
