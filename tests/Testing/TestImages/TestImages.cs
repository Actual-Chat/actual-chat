using ActualChat.Uploads;

namespace ActualChat.Testing;

public static class TestImages
{
    public const string DefaultJpg = "default.jpg";

    public static Stream GetImage(string name)
    {
        var type = typeof(TestImages);
        return type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestImages.{name}").Require();
    }

    public static UploadedStreamFile GetUploadedImage(string name)
    {
        var stream = GetImage(name);
        return new UploadedStreamFile(name, "image/jpeg", stream.Length, () => Task.FromResult(stream));
    }
}
