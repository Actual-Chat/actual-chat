namespace ActualChat.Media.Resources;

public class Resource(string name)
{
    public static readonly Resource AlumniPng = new ("alumni.png");
    public static readonly Resource CoworkersPng = new ("coworkers.png");
    public static readonly Resource FamilyPng = new ("family.png");
    public static readonly Resource NotesPng = new ("notes.png");
    public static readonly Resource FriendsPng = new ("friends.png");
    public static readonly Resource SherlockPng = new ("sherlock.png");

    public string Name { get; } = name;

    public Stream GetStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{typeof(Resource).Namespace}.Converted.{Name}";
        var stream = assembly.GetManifestResourceStream(resourceName);
        return stream ?? throw StandardError.Internal($"Resource is not found: {Name}.");
    }
}
