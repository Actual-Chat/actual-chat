namespace ActualChat.Users.Templates;

public class Template(string name)
{
    public static readonly Template EmailVerification = new ("EmailVerification.mjml");

    public string Name { get; } = name;

    public Stream GetStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(typeof(Template), Name);
        if (stream != null)
            return stream;

        throw StandardError.Internal($"Template is not found: {Name}.");
    }
}
