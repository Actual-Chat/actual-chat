using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ActualChat.Mcp.IntegrationTests;

public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
