namespace ActualChat
{
    public interface IVersionProvider<TVersion>
        where TVersion : notnull
    {
        TVersion NextVersion(TVersion currentVersion);
    }
}
