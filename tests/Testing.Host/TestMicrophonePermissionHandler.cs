using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Testing.Host;

/// <summary>
/// A <see cref="MicrophonePermissionHandler"/> for a <see cref="BlazorTester"/>, which has no JS to ask:
/// the test sets what the browser would answer.
/// </summary>
public sealed class TestMicrophonePermissionHandler(UIHub hub) : MicrophonePermissionHandler(hub, false)
{
    public bool? GetResult { get; set; }
    public bool RequestResult { get; set; }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
        => Task.FromResult(GetResult);

    protected override Task<bool> Request(CancellationToken cancellationToken)
        => Task.FromResult(RequestResult);

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
