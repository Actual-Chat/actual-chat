namespace ActualChat.Media.Resources;

public class Resource(string name)
{
    public static readonly Resource AlumniSvg = new ("alumni.svg");
    public static readonly Resource CoworkersSvg = new ("coworkers.svg");
    public static readonly Resource FamilySvg = new ("family.svg");
    public static readonly Resource NotesSvg = new ("notes.svg");
    public static readonly Resource FriendsSvg = new ("friends.svg");

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
