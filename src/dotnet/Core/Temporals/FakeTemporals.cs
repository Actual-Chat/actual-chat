namespace ActualChat;

public class FakeTemporals : Temporals
{
    public static readonly Temporals Instance = new FakeTemporals();

    private FakeTemporals()
        => IsReal = false;

    protected sealed override ValueTask<Entry?> GetEntry(string key)
        => default;

    protected sealed override void SetEntry<T>(string key, T value, TimeSpan expiresIn)
    { }

    protected sealed override void RemoveEntry(string key)
    { }
}
