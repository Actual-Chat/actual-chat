using ActualLab.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;
using ActualLab.Rpc.Serialization;
using ActualLab.Rpc.WebSockets;

namespace ActualChat.Media.UnitTests;

public class MediaSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Session TestSession = Session.New();
    private static readonly UserId TestUserId = UserId.New();

    [Fact]
    public void Media_Basic()
    {
        var mediaId = MediaId.New(TestUserId.Value, "local1");
        var media = new Media(mediaId, "content-1", 0, MediaKind.Unknown, new MetadataBag());

        var s = media.PassThroughSerializers(Out);
        s.Id.Should().Be(media.Id);
        s.BlobId.Should().Be(media.BlobId);
    }

    [Fact]
    public void LinkPreview_Basic()
    {
        var preview = new LinkPreview {
            Id = "preview-1",
            Version = 1,
            Url = "https://example.com",
            Title = "Example",
            Description = "An example page",
            CreatedAt = new Moment(DateTime.UtcNow),
            ModifiedAt = new Moment(DateTime.UtcNow),
        };

        var s = preview.PassThroughSerializers(Out);
        s.Id.Should().Be(preview.Id);
        s.Url.Should().Be(preview.Url);
        s.Title.Should().Be(preview.Title);
        s.Description.Should().Be(preview.Description);
    }

    [Fact]
    public void GrabStatus_Basic()
    {
        var status = new GrabStatus("grab-1", 1) {
            IsSuccessful = true,
            ModifiedAt = new Moment(DateTime.UtcNow),
        };

        var s = status.PassThroughSerializers(Out);
        s.Id.Should().Be(status.Id);
        s.IsSuccessful.Should().Be(status.IsSuccessful);
    }

    [Fact]
    public void MediaRef_Basic()
    {
        var mediaId = MediaId.New(TestUserId.Value, "local1");
        var content = new MediaRef(mediaId, "content-1");
        var s = content.PassThroughSerializers(Out);
        s.MediaId.Should().Be(content.MediaId);
        s.BlobId.Should().Be(content.BlobId);
    }

    [Fact]
    public void MediaRef_WithThumbnail()
    {
        var mediaId = MediaId.New(TestUserId.Value, "local1");
        var thumbId = MediaId.New(TestUserId.Value, "thumb1");
        var content = new MediaRef(mediaId, "content-1", thumbId, "thumb-content-1");
        var s = content.PassThroughSerializers(Out);
        s.MediaId.Should().Be(content.MediaId);
        s.BlobId.Should().Be(content.BlobId);
        s.ThumbnailMediaId.Should().Be(content.ThumbnailMediaId);
        s.ThumbnailBlobId.Should().Be(content.ThumbnailBlobId);
    }

    [Fact]
    public void Picture_Basic()
    {
        var mediaId = MediaId.New(TestUserId.Value, "local1");
        var content = new MediaRef(mediaId, "content-1");
        var picture = new Picture(content, "https://example.com/pic.jpg", "avatar-key");
        var s = picture.PassThroughSerializers(Out);
        s.MediaRef.Should().NotBeNull();
        s.MediaRef!.MediaId.Should().Be(picture.MediaRef!.MediaId);
        s.ExternalUrl.Should().Be(picture.ExternalUrl);
        s.AvatarKey.Should().Be(picture.AvatarKey);
    }

    [Fact]
    public void Picture_ExternalOnly()
    {
        var picture = new Picture(null, "https://example.com/pic.jpg");
        var s = picture.PassThroughSerializers(Out);
        s.MediaRef.Should().BeNull();
        s.ExternalUrl.Should().Be(picture.ExternalUrl);
    }

    // Upload commands

    [Fact]
    public void Uploads_Create_Basic()
    {
        var cmd = new Uploads_Create(TestSession, 1024, "image", new MetadataBag());
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void Uploads_Remove_Basic()
    {
        var uploadId = UploadId.New();
        var cmd = new Uploads_Remove(TestSession, uploadId);
        cmd.AssertPassesThroughSerializers();
    }

    [Fact]
    public void LimitsRpcArgumentDataSize()
    {
        // act
        var textLimit = RpcTextMessageSerializer.Defaults.MaxArgumentDataSize;
        var byteLimit = RpcByteMessageSerializer.Defaults.MaxArgumentDataSize;
        var transportLimit = ApiConstants.Rpc.MaxMessageSize;
        var frameLimit = RpcWebSocketTransport.Options.Default.MaxMessageSize;
        Out.WriteLine($"text = {textLimit}, byte = {byteLimit}, transport = {transportLimit}, frame = {frameLimit}");

        // assert
        textLimit.Should().Be(ApiConstants.Rpc.MaxArgumentDataSize);
        byteLimit.Should().Be(ApiConstants.Rpc.MaxArgumentDataSize);
        textLimit.Should().BeLessThan(130_000_000);
        transportLimit.Should().Be(
            RpcTextMessageSerializerV3.GetMaxMessageSize(ApiConstants.Rpc.MaxArgumentDataSize));
        transportLimit.Should().BeLessThan(frameLimit);
    }

    [Fact]
    public void AcceptsRealisticPayload()
    {
        // arrange
        var command = new Uploads_Append(TestSession, UploadId.New(), 0, new byte[4 * 1024 * 1024]);
        var arguments = ArgumentList.New(command);
        var formats = new[] {
            RpcSerializationFormat.MessagePackV6,
            RpcSerializationFormat.SystemJsonV5,
        };
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddFusion().AddClient<IUploads>();
        using var services = serviceCollection.BuildServiceProvider();
        var hub = services.RpcHub();
        var method = hub.ServiceRegistry[typeof(IUploads)].Methods
            .Single(x => x.MethodInfo.Name == nameof(IUploads.OnAppend));
        var sizes = new List<(string Format, int ArgumentData, int Message)>();

        // act
        foreach (var format in formats) {
            var peer = new RpcClientPeer(hub, RpcRef.NewClient("payload-test", format.Key).Route);
            using var argumentBuffer = new ArrayPoolBuffer<byte>(mustClear: false);
            format.ArgumentSerializer.Serialize(arguments, false, argumentBuffer);
            var argumentData = argumentBuffer.WrittenMemory;
            var context = new RpcOutboundContext(peer) {
                Arguments = arguments,
                MethodDef = method,
            };
            var message = new RpcOutboundMessage(context, method, 1, false, null, argumentData);
            using var messageBuffer = new ArrayPoolBuffer<byte>(mustClear: false);
            format.MessageSerializerFactory.Invoke(peer).Write(messageBuffer, message);
            sizes.Add((format.Key, argumentData.Length, messageBuffer.WrittenCount));
        }

        // assert
        foreach (var size in sizes) {
            Out.WriteLine($"{size.Format}: argument data = {size.ArgumentData}, message = {size.Message}");
            size.ArgumentData.Should().BeLessThan(ApiConstants.Rpc.MaxArgumentDataSize);
            size.Message.Should().BeLessThanOrEqualTo(RpcWebSocketTransport.Options.Default.MaxMessageSize);
        }
    }
}
