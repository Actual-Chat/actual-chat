using ActualChat.Rpc;

namespace ActualChat.Maui;

public sealed class MauiRpcEndpointSelector(string[] candidates, string? current)
    : RpcEndpointSelector(candidates, current)
{
    public static void Use()
        => Instance = new MauiRpcEndpointSelector(MauiSettings.RpcEndpoints, MauiPreferences.RpcEndpoint);

    // Protected/internal methods

    protected override void OnChanged(string endpoint)
        => MauiPreferences.RpcEndpoint = IsOnOrigin ? null : endpoint;
}
