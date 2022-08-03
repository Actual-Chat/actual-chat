namespace ActualChat.Kvas;

public class KvasForSecondary : IKvas
{
    public IKvas Primary { get; }
    public IKvas Secondary { get; }

    public KvasForSecondary(IKvas primary, IKvas secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public async ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default)
        => await Primary.Get(key, cancellationToken).ConfigureAwait(false)
            ?? await Secondary.Get(key, cancellationToken).ConfigureAwait(false);

    public void Set(Symbol key, string? value)
    {
        Primary.Set(key, value);
        Secondary.Set(key, value);
    }

    public async Task Flush(CancellationToken cancellationToken = default)
    {
        var task1 = Primary.Flush(cancellationToken);
        var task2 = Secondary.Flush(cancellationToken);
        await task1.ConfigureAwait(false);
        await task2.ConfigureAwait(false);
    }
}
