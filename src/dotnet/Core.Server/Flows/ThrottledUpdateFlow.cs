namespace ActualChat.Flows;

public abstract class ThrottledUpdateFlow : ThrottledFlow
{
    protected string Target => Id.Arguments.FromBase64();

    public static string GetArguments(string target)
        => target.ToBase64();

    public bool MustUpdate(Moment now)
        => NextRunAt <= now;
}
