namespace ActualChat.UI.Blazor.Services;

public sealed class BannerInstance : IDisposable
{
    public RenderFragment View { get; internal set; } = null!;
    private Action<BannerInstance> Disposer { get; }

    public BannerInstance(Action<BannerInstance> disposer)
        => Disposer = disposer;

    public void Dispose()
        => Close();

    public void Close()
        => Disposer(this);
}
