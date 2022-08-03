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

    public async Task Set(Symbol key, string? value, CancellationToken cancellationToken = default)
    {
        var task1 = Primary.Set(key, value, cancellationToken);
        var task2 = Secondary.Set(key, value, cancellationToken);
        await task1.ConfigureAwait(false);
        await task2.ConfigureAwait(false);
    }

    public async Task Flush(CancellationToken cancellationToken = default)
    {
        var task1 = Primary.Flush(cancellationToken);
        var task2 = Secondary.Flush(cancellationToken);
        await task1.ConfigureAwait(false);
        await task2.ConfigureAwait(false);
    }
}
