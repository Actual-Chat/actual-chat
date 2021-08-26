namespace ActualChat
{
    public interface IHasVersion<out TVersion>
        where TVersion : notnull
    {
        TVersion Version { get; }
    }

    public interface IHasWritableVersion<TVersion> : IHasVersion<TVersion>
        where TVersion : notnull
    {
        new TVersion Version { get; set; }
    }
}
