namespace ActualChat.Kvass;

public interface IKvass
{
    ValueTask<string?> Get(string key, CancellationToken cancellationToken = default);
    ValueTask Set(string key, string? value, CancellationToken cancellationToken = default);
}
