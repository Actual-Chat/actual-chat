using System.Net;
using System.Text;

namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Fact]
    public async Task AnActiveFillShouldBeSeekable()
    {
        // arrange
        using var source = new GatedContentStream();
        var content = new StreamContent(source);
        content.Headers.ContentLength = 8;
        var handler = Create(new TestSource(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        // act
        using var response = await handler.Handle(Request());
        var body = await response!.Content.ReadAsStreamAsync();

        // assert
        body.CanSeek.Should().BeTrue("WebView2 rejects a response stream that cannot seek");
        body.Length.Should().Be(8);
        body.Seek(4, SeekOrigin.Begin);
        var buffer = new byte[4];
        var readTask = body.ReadExactlyAsync(buffer).AsTask();
        readTask.IsCompleted.Should().BeFalse("the fill has not reached that position yet");
        source.Release();
        await readTask;
        Encoding.UTF8.GetString(buffer).Should().Be("tail");
    }

    [Fact]
    public async Task SeekingBackAndForthShouldReturnTheSameBytes()
    {
        // arrange
        const string body = "0123456789";
        var handler = Create(new TestSource(_ => Response(body)));
        using (var filled = await handler.Handle(Request()))
            await filled!.Content.ReadAsStringAsync();

        // act
        using var hit = await handler.Handle(Request());
        var stream = await hit!.Content.ReadAsStreamAsync();
        var buffer = new byte[3];
        stream.Seek(7, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);
        var tail = Encoding.UTF8.GetString(buffer);
        stream.Seek(0, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);
        var head = Encoding.UTF8.GetString(buffer);

        // assert
        tail.Should().Be("789");
        head.Should().Be("012");
        stream.Position.Should().Be(3);
    }

    [Fact]
    public async Task SeekingOutsideTheContentShouldThrow()
    {
        // arrange
        var handler = Create(new TestSource(_ => Response("0123456789")));
        using (var filled = await handler.Handle(Request()))
            await filled!.Content.ReadAsStringAsync();
        using var hit = await handler.Handle(Request());
        var stream = await hit!.Content.ReadAsStreamAsync();

        // act
        var seekPastEnd = () => stream.Seek(11, SeekOrigin.Begin);
        var seekBeforeStart = () => stream.Seek(-1, SeekOrigin.Begin);

        // assert
        seekPastEnd.Should().Throw<ArgumentOutOfRangeException>();
        seekBeforeStart.Should().Throw<ArgumentOutOfRangeException>();
    }
}
