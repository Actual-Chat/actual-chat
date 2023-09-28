namespace ActualChat.Media.Resources;

public class Resource(string name)
{
    public static Resource AlimniSvg = new ("alumni.svg");
    public static Resource CoworkersSvg = new ("coworkers.svg");
    public static Resource FamilySvg = new ("family.svg");
    public static Resource NotesSvg = new ("notes.svg");
    public static Resource FriendsSvg = new ("friends.svg");

    public string Name { get; } = name;

    public Stream GetStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(typeof(Resource), Name);
        if (stream != null)
            return stream;

        throw StandardError.Internal($"Resource is not found: {Name}.");
    }
}
